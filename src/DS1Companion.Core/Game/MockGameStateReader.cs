using DS1Companion.Core.Data;
using DS1Companion.Core.Logic;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Game;

/// <summary>
/// Встроенный демо-режим: изменяемое состояние «игры» для проверки всего UI без запущенной игры.
/// Стартовое состояние берётся из game.json (раздел demo), поэтому работает для любой игры серии.
/// </summary>
public sealed class MockGameStateReader : IGameStateReader
{
    private const int DemoWeaponBaseId = 900_000;

    private readonly object _gate = new();
    private readonly KnowledgeBase _kb;
    private readonly Random _random;
    private readonly List<InventoryEntry> _inventory = new();
    private GameSnapshot _state;
    private int _locationIndex;
    private int _weaponLevel;
    private readonly string? _weaponItemId;

    public string Name => "Демо";
    public GameDefinition Game => _kb.Game;

    public MockGameStateReader(KnowledgeBase kb, int seed = 42)
    {
        _kb = kb;
        _random = new Random(seed);
        var demo = kb.Game.Demo;
        _locationIndex = Math.Max(0, kb.Locations.ToList().FindIndex(l => l.Id == demo.LocationId));
        _weaponItemId = demo.WeaponItemId;
        _weaponLevel = demo.WeaponLevel;

        if (demo.HealingItemId is { } heal) _inventory.Add(Entry(heal, demo.HealingCount));
        if (_weaponItemId is not null) _inventory.Add(Entry(_weaponItemId, 1));

        _state = new GameSnapshot
        {
            IsConnected = true,
            Game = kb.Game.Key,
            CharacterName = demo.CharacterName,
            Deaths = demo.Deaths,
            LocationId = kb.Locations.Count > 0 ? kb.Locations[_locationIndex].Id : null,
            Souls = demo.Souls,
            Level = kb.Game.HasLevels ? demo.Level : null,
            SoulsForNextLevel = kb.Game.LevelCurve == "memory" ? MemoryCurveCost(demo.Level) : null,
            HollowLevel = kb.Game.HasHollowing ? 0 : null,
            Stats = demo.Stats.Count > 0 ? CharacterStats.From(demo.Stats) : null,
            Weapon = WeaponSnapshot(demo.WeaponMaxDurability * 95 / 100, demo.WeaponMaxDurability),
            Inventory = _inventory.ToList(),
        };
    }

    /// <summary>Для игр, где стоимость уровня берётся из памяти: правдоподобная демо-кривая.</summary>
    private static int MemoryCurveCost(int level) => LevelCurve.Souls3Cost(level);

    private InventoryEntry Entry(string itemId, int qty)
    {
        var item = _kb.FindItem(itemId);
        return new InventoryEntry(item?.GameKind ?? ItemKind.Goods, item?.GameId ?? 0, qty, itemId);
    }

    private EquippedWeapon? WeaponSnapshot(int durability, int max)
    {
        if (_weaponItemId is null || (!_kb.Game.HasWeaponUpgrades && !_kb.Game.HasDurability)) return null;
        var baseId = _kb.FindItem(_weaponItemId)?.GameId ?? DemoWeaponBaseId;
        return new EquippedWeapon
        {
            RawId = WeaponIdCodec.Compose(baseId, 0, _weaponLevel),
            ItemId = _weaponItemId,
            Durability = _kb.Game.HasDurability ? durability : 0,
            MaxDurability = _kb.Game.HasDurability ? max : 0,
        };
    }

    public GameSnapshot Read()
    {
        lock (_gate) return _state with { Inventory = _inventory.ToList() };
    }

    private void Update(Func<GameSnapshot, GameSnapshot> change)
    {
        lock (_gate) _state = change(_state);
    }

    public ItemInfo? PickUp(string itemId, int quantity = 1)
    {
        var item = _kb.FindItem(itemId);
        if (item is null) return null;
        lock (_gate)
        {
            var idx = _inventory.FindIndex(e => e.ItemId == itemId);
            if (idx >= 0 && item.GameKind is null or ItemKind.Goods)
                _inventory[idx] = _inventory[idx] with { Quantity = _inventory[idx].Quantity + quantity };
            else
                _inventory.Add(Entry(itemId, quantity));
        }
        return item;
    }

    public ItemInfo? PickUpRandom(Rarity rarity)
    {
        var pool = _kb.Items.Where(i => i.Rarity == rarity).ToList();
        return pool.Count == 0 ? null : PickUp(pool[_random.Next(pool.Count)].Id);
    }

    public ItemInfo? PickUpLegendary() =>
        (_kb.Game.Demo.LegendaryItemId is { } id ? PickUp(id) : null) ?? PickUpRandom(Rarity.Legendary);

    public ItemInfo? PickUpKey() =>
        (_kb.Game.Demo.KeyItemId is { } id ? PickUp(id) : null)
        ?? (_kb.Items.FirstOrDefault(i => i.Category == ItemCategory.KeyItem) is { } k ? PickUp(k.Id) : null);

    public void AddSouls(int amount) => Update(s => s with { Souls = Math.Max(0, (s.Souls ?? 0) + amount) });

    /// <summary>Смерть: валюта уходит в кровавое пятно (старое сгорает), в DS2 растёт Опустошение.</summary>
    public void Die() => Update(s => s with
    {
        Deaths = (s.Deaths ?? 0) + 1,
        BloodstainSouls = _kb.Game.HasBloodstain ? s.Souls ?? 0 : null,
        Souls = _kb.Game.HasBloodstain ? 0 : (s.Souls ?? 0) / 2, // в Sekiro теряется половина сэн
        HollowLevel = s.HollowLevel is { } h ? h + 1 : null,
        InBossFight = false,
    });

    public void RecoverBloodstain() => Update(s => s with
    {
        Souls = (s.Souls ?? 0) + (s.BloodstainSouls ?? 0),
        BloodstainSouls = s.BloodstainSouls is null ? null : 0,
    });

    /// <summary>Восстановить облик (Чучело человека в DS2).</summary>
    public void RestoreHumanity() => Update(s => s with { HollowLevel = s.HollowLevel is null ? null : 0 });

    public void DamageWeapon(double fraction = 0.25) => Update(s => s.Weapon is not { MaxDurability: > 0 } w ? s : s with
    {
        Weapon = w with { Durability = Math.Max(0, w.Durability - (int)(w.MaxDurability * fraction)) },
    });

    public void RepairWeapon() => Update(s => s.Weapon is null ? s : s with
    {
        Weapon = s.Weapon with { Durability = s.Weapon.MaxDurability },
    });

    public void UpgradeWeapon()
    {
        lock (_gate)
        {
            var max = _kb.FindItem(_weaponItemId ?? "") is { } item && _kb.FindWeaponUpgrade(item.Id) is { } w
                      && _kb.FindPath(w.PathId) is { } p ? p.MaxLevel : 15;
            _weaponLevel = _weaponLevel >= max ? 0 : _weaponLevel + 1;
            if (_state.Weapon is { } cur)
                _state = _state with { Weapon = WeaponSnapshot(cur.Durability, cur.MaxDurability) };
        }
    }

    public void LevelUp()
    {
        Update(s =>
        {
            if (s.Level is not { } level) return s;
            var cost = s.SoulsForNextLevel ?? LevelCurve.CostForNextLevel(_kb.Game, level) ?? 0;
            if (cost <= 0 || (s.Souls ?? 0) < cost) return s;
            var stats = s.Stats;
            if (stats is not null && _kb.Game.Stats.FirstOrDefault() is { } first) stats = stats.With(first.Key, stats[first.Key] + 1);
            return s with
            {
                Level = level + 1,
                Souls = s.Souls - cost,
                Stats = stats,
                SoulsForNextLevel = s.SoulsForNextLevel is null ? null : MemoryCurveCost(level + 1),
            };
        });
    }

    public LocationInfo? NextLocation()
    {
        if (_kb.Locations.Count == 0) return null;
        _locationIndex = (_locationIndex + 1) % _kb.Locations.Count;
        var loc = _kb.Locations[_locationIndex];
        Update(s => s with { LocationId = loc.Id, InBossFight = false });
        return loc;
    }

    public BossInfo? StartBossFight()
    {
        var current = Read().LocationId;
        var boss = (current is null ? null : _kb.BossesIn(current).FirstOrDefault()) ?? _kb.Bosses.FirstOrDefault();
        if (boss is null) return null;
        Update(s => s with { InBossFight = true, BossId = boss.Id });
        return boss;
    }

    public void EndBossFight(bool victory)
    {
        if (victory) Update(s => s with { InBossFight = false, Souls = (s.Souls ?? 0) + 3_000 });
        else Die();
    }

    public void Dispose() { }
}
