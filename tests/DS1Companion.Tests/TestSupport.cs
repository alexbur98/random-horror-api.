using DS1Companion.Core.Data;
using DS1Companion.Core.Memory;
using DS1Companion.Core.Models;
using DS1Companion.Core.Persistence;
using DS1Companion.Core.Presentation;
using DS1Companion.Core.Services;

namespace DS1Companion.Tests;

public static class TestData
{
    private static readonly Lazy<KnowledgeBase> Kb = new(() => KnowledgeBase.LoadFromDirectory(DataDir));

    public static string DataDir => Path.Combine(AppContext.BaseDirectory, "Data");
    public static KnowledgeBase Knowledge => Kb.Value;

    public static string SolutionDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DS1Companion.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("DS1Companion.sln не найден");
        }
    }

    public static ItemInfo Item(string id, Rarity rarity = Rarity.Common, ItemCategory category = ItemCategory.Consumable,
        string? iconKey = null, string? iconUrl = null) => new()
    {
        Id = id, NameRu = id, NameEn = id, Rarity = rarity, Category = category, IconKey = iconKey, IconUrl = iconUrl,
        GameKind = ItemKind.Goods, GameId = 1,
    };
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ds1c_" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch { /* ignore */ }
    }
}

public sealed class FakeClock : IClock
{
    public DateTime Now { get; set; } = new(2026, 1, 1, 12, 0, 0);
    public void Advance(TimeSpan t) => Now += t;
}

public sealed class FakeIconProvider : IIconProvider
{
    public event Action<string>? IconUpdated;
    public IconSource Resolve(ItemInfo item) => IconSource.ForGlyph(item.EffectiveGlyph);
    public void RaiseUpdated(string id) => IconUpdated?.Invoke(id);
}

/// <summary>Фейковая память процесса из набора регионов.</summary>
public sealed class FakeMemory : IProcessMemory
{
    private readonly SortedDictionary<long, byte[]> _regions = new();

    public bool IsAlive { get; set; } = true;
    public long ModuleBase { get; } = 0x1_4000_0000;
    public int ModuleSize { get; }
    public int ReadCount { get; private set; }

    public FakeMemory(int moduleSize = 0x10000)
    {
        ModuleSize = moduleSize;
        AddRegion(ModuleBase, ModuleSize);
    }

    public void AddRegion(long address, int size) => _regions[address] = new byte[size];

    public void Write(long address, ReadOnlySpan<byte> data)
    {
        var (start, bytes) = Find(address, data.Length) ?? throw new ArgumentException($"Нет региона 0x{address:X}");
        data.CopyTo(bytes.AsSpan((int)(address - start)));
    }

    public void WriteInt32(long a, int v) => Write(a, BitConverter.GetBytes(v));
    public void WriteInt64(long a, long v) => Write(a, BitConverter.GetBytes(v));

    public bool TryRead(long address, Span<byte> buffer)
    {
        ReadCount++;
        if (Find(address, buffer.Length) is not { } hit) return false;
        var (start, bytes) = hit;
        bytes.AsSpan((int)(address - start), buffer.Length).CopyTo(buffer);
        return true;
    }

    private (long Start, byte[] Bytes)? Find(long address, int length)
    {
        foreach (var (start, bytes) in _regions)
            if (address >= start && address + length <= start + bytes.Length) return (start, bytes);
        return null;
    }

    public void Dispose() => IsAlive = false;
}

public static class VmFactory
{
    public static (MainViewModel Vm, NullSoundService Sound, FakeClock Clock, AppSettings Settings, ProfileStore Profiles) Create(
        TempDir dir, bool demo = true, Rarity minRarity = Rarity.Common)
    {
        var paths = new AppPaths(dir.Path);
        paths.EnsureCreated();
        var settings = new AppSettings { StartInDemo = demo, PopupMinRarity = minRarity };
        var sound = new NullSoundService();
        var clock = new FakeClock();
        var profiles = new ProfileStore(paths);
        var store = new SettingsStore(paths);
        var vm = new MainViewModel(new MainViewModelDependencies
        {
            Knowledge = TestData.Knowledge,
            Sound = sound,
            Icons = new FakeIconProvider(),
            Settings = settings,
            SaveSettings = store.Save,
            Profiles = profiles,
            Clock = clock,
            CreateLiveReader = () => new DS1Companion.Core.Game.DisconnectedReader(),
        });
        return (vm, sound, clock, settings, profiles);
    }

    /// <summary>Один цикл опроса: читаем снимок текущего ридера и отдаём во ViewModel.</summary>
    public static void Poll(this MainViewModel vm) => vm.OnSnapshot(vm.CurrentReader, vm.CurrentReader.Read());
}
