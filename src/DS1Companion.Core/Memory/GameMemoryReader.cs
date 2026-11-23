using DS1Companion.Core.Game;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Memory;

/// <summary>
/// Строго read-only чтение памяти игры (x64). Базовые адреса находятся AOB-сканированием,
/// данные — по цепочкам указателей из Data/{игра}/offsets.json. Если какое-то поле не читается
/// (другая версия игры, главное меню) — оно просто будет null.
/// </summary>
public sealed class GameMemoryReader : IGameStateReader
{
    private static readonly TimeSpan AttachRetry = TimeSpan.FromSeconds(2);

    private readonly OffsetsConfig _config;
    private readonly GameDefinition _game;
    private readonly Func<IProcessMemory?> _attach;
    private readonly Dictionary<string, long> _bases = new();
    private IProcessMemory? _memory;
    private DateTime _nextAttach = DateTime.MinValue;

    public string Name => _game.ShortTitle;
    public GameDefinition Game => _game;
    public bool IsAttached => _memory is { IsAlive: true };
    public IReadOnlyDictionary<string, long> ResolvedBases => _bases;
    public event Action<string>? Log;

    public GameMemoryReader(GameDefinition game, OffsetsConfig config, Func<IProcessMemory?>? attach = null)
    {
        _game = game;
        _config = config;
        _attach = attach ?? (() => WindowsProcessMemory.TryOpen(config.ProcessName));
    }

    private readonly object _gate = new();
    private bool _disposed;

    public GameSnapshot Read()
    {
        lock (_gate)
        {
            if (_disposed) return GameSnapshot.Disconnected;
            try
            {
                if (!EnsureAttached() || _memory is not { } memory) return GameSnapshot.Disconnected;
                return ReadSnapshot(memory);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Ошибка чтения: {ex.Message}");
                Detach();
                return GameSnapshot.Disconnected;
            }
        }
    }

    private bool EnsureAttached()
    {
        if (_memory is { IsAlive: true }) return true;
        Detach();
        var now = DateTime.UtcNow;
        if (now < _nextAttach) return false;
        _nextAttach = now + AttachRetry;

        _memory = _attach();
        if (_memory is null) return false;

        foreach (var (name, def) in _config.Bases)
        {
            var hit = AobScanner.ScanModule(_memory, AobPattern.Parse(def.Aob));
            var resolved = hit is null ? null : AobScanner.ResolveRipRelative(_memory, hit.Value, def.RipOffset, def.InstructionLength);
            if (resolved is { } addr)
            {
                _bases[name] = addr;
                Log?.Invoke($"База '{name}' найдена: 0x{addr:X}");
            }
            else
            {
                Log?.Invoke($"База '{name}' НЕ найдена — проверьте AOB в offsets.json");
            }
        }
        return true;
    }

    private void Detach()
    {
        _memory?.Dispose();
        _memory = null;
        _bases.Clear();
    }

    private long? Address(IProcessMemory m, FieldDefinition f) =>
        _bases.TryGetValue(f.Base, out var b) ? PointerChain.Resolve(m, b, f.ParsedOffsets()) : null;

    private int? Int(IProcessMemory m, string field)
    {
        if (!_config.Fields.TryGetValue(field, out var f) || Address(m, f) is not { } a) return null;
        return f.Type switch
        {
            FieldType.Byte => m.ReadByte(a),
            FieldType.Int64 => (int?)m.ReadInt64(a),
            _ => m.ReadInt32(a),
        };
    }

    /// <summary>Защита от неверных смещений: значение вне правдоподобного диапазона считается непрочитанным.</summary>
    private int? Sane(IProcessMemory m, string field, int min, int max) =>
        Int(m, field) is { } v && v >= min && v <= max ? v : null;

    private int? Stat(IProcessMemory m, string field) => Sane(m, field, 1, 99);

    private string? Text(IProcessMemory m, string field)
    {
        if (!_config.Fields.TryGetValue(field, out var f) || Address(m, f) is not { } a) return null;
        var s = m.ReadUtf16(a, Math.Clamp(f.Length, 1, 64));
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    internal GameSnapshot ReadSnapshot(IProcessMemory m)
    {
        CharacterStats? stats = null;
        var statValues = _game.Stats
            .Select(d => (d.Key, Value: Stat(m, d.Key)))
            .Where(x => x.Value is not null)
            .ToDictionary(x => x.Key, x => x.Value!.Value);
        if (statValues.Count > 0) stats = new CharacterStats { Values = statValues };

        EquippedWeapon? weapon = null;
        if (Sane(m, "weapon_id", 0, 9_999_999) is { } wid)
        {
            var max = Sane(m, "weapon_max_durability", 1, 10_000) ?? 0;
            weapon = new EquippedWeapon
            {
                RawId = wid,
                Durability = Math.Min(Sane(m, "weapon_durability", 0, 10_000) ?? max, max == 0 ? int.MaxValue : max),
                MaxDurability = max,
            };
        }

        var name = Text(m, "character_name");
        if (name is not null && name.Any(char.IsControl)) name = null;

        return new GameSnapshot
        {
            IsConnected = true,
            Game = _game.Key,
            SoulsForNextLevel = Sane(m, "souls_for_next_level", 1, 999_999_999),
            HollowLevel = Sane(m, "hollow_level", 0, 1000),
            CharacterName = name,
            Deaths = Sane(m, "deaths", 0, 10_000_000),
            Area = Sane(m, "area", 1, 99),
            Block = Sane(m, "block", 0, 9),
            Souls = Sane(m, "souls", 0, 999_999_999),
            Level = Sane(m, "level", 1, Math.Max(1, _game.MaxLevel)),
            Stats = stats,
            Weapon = weapon,
            BloodstainSouls = Sane(m, "bloodstain_souls", 0, 999_999_999),
            Inventory = ReadInventory(m),
            InBossFight = Int(m, "boss_fight_active") is 1,
        };
    }

    internal IReadOnlyList<InventoryEntry>? ReadInventory(IProcessMemory m)
    {
        var inv = _config.Inventory;
        if (inv is null || !_bases.TryGetValue(inv.Base, out var b)) return null;

        var listAddr = PointerChain.Resolve(m, b, inv.ListOffsets.Select(OffsetsConfig.ParseOffset).ToList());
        if (listAddr is null) return null;

        int count = inv.Capacity;
        if (inv.CountOffsets is { Count: > 0 } co)
        {
            var countAddr = PointerChain.Resolve(m, b, co.Select(OffsetsConfig.ParseOffset).ToList());
            if (countAddr is null || m.ReadInt32(countAddr.Value) is not { } c) return null;
            count = c;
        }
        if (count < 0 || count > inv.Capacity || inv.EntrySize <= 0) return null;

        var raw = new byte[count * inv.EntrySize];
        if (count > 0 && !m.TryRead(listAddr.Value, raw)) return null;

        var result = new List<InventoryEntry>(count);
        for (int i = 0; i < count; i++)
        {
            var span = raw.AsSpan(i * inv.EntrySize, inv.EntrySize);
            int category = BitConverter.ToInt32(span[inv.CategoryOffset..]);
            int id = BitConverter.ToInt32(span[inv.IdOffset..]);
            int qty = BitConverter.ToInt32(span[inv.QuantityOffset..]);
            if (id < 0 || qty <= 0) continue;
            var kind = ((uint)category >> 28) switch
            {
                0 => ItemKind.Weapon,
                1 => ItemKind.Armor,
                2 => ItemKind.Ring,
                4 => ItemKind.Goods,
                _ => (ItemKind?)null,
            };
            if (kind is { } k) result.Add(new InventoryEntry(k, id, qty));
        }
        return result;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Detach();
        }
    }
}
