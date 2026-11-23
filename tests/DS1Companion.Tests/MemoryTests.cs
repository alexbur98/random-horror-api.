using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DS1Companion.Core.Memory;
using DS1Companion.Core.Models;

namespace DS1Companion.Tests;

public class AobScannerTests
{
    [Fact]
    public void Parse_SupportsWildcards()
    {
        var p = AobPattern.Parse("48 8B 05 ?? ? 00 C3");
        Assert.Equal(7, p.Length);
        Assert.Equal(new[] { true, true, true, false, false, true, true }, p.Mask);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?? ??")]
    [InlineData("4G 00")]
    [InlineData("123")]
    public void Parse_RejectsInvalid(string pattern) => Assert.Throws<FormatException>(() => AobPattern.Parse(pattern));

    [Fact]
    public void Find_LocatesPatternWithWildcards()
    {
        var data = new byte[] { 0x00, 0x48, 0x8B, 0x05, 0x48, 0x8B, 0x05, 0x11, 0x22, 0xC3, 0x90 };
        Assert.Equal(4, AobScanner.Find(data, AobPattern.Parse("48 8B 05 ?? ?? C3")));
        Assert.Equal(-1, AobScanner.Find(data, AobPattern.Parse("48 8B 05 ?? ?? C4")));
        Assert.Equal(-1, AobScanner.Find(new byte[] { 0x48 }, AobPattern.Parse("48 8B")));
    }

    [Fact]
    public void ScanModule_FindsPatternAcrossChunkBoundary()
    {
        var mem = new FakeMemory();
        var at = mem.ModuleBase + 0x1000 - 2; // шаблон пересекает границу чанка 0x1000
        mem.Write(at, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Equal(at, AobScanner.ScanModule(mem, AobPattern.Parse("DE AD BE EF"), chunkSize: 0x1000));
        Assert.Null(AobScanner.ScanModule(mem, AobPattern.Parse("DE AD BE EE"), chunkSize: 0x1000));
    }

    [Fact]
    public void RipRelative_And_PointerChain()
    {
        var mem = new FakeMemory();
        long instr = mem.ModuleBase + 0x100;
        long staticPtr = mem.ModuleBase + 0x8000;
        mem.WriteInt32(instr + 3, (int)(staticPtr - (instr + 7)));
        Assert.Equal(staticPtr, AobScanner.ResolveRipRelative(mem, instr, 3, 7));

        const long heap = 0x2_0000_0000;
        mem.AddRegion(heap, 0x1000);
        mem.WriteInt64(staticPtr, heap);
        mem.WriteInt64(heap + 0x10, heap + 0x800);
        Assert.Equal(heap + 0x800 + 0x94, PointerChain.Resolve(mem, staticPtr, new long[] { 0x10, 0x94 }));
        Assert.Equal(heap + 0x98, PointerChain.Resolve(mem, staticPtr, new long[] { 0x98 }));
        Assert.Null(PointerChain.Resolve(mem, staticPtr, new long[] { 0x20, 0x0 })); // нулевой указатель
    }

    [Theory]
    [InlineData("0x10", 16)]
    [InlineData("0XA8", 168)]
    [InlineData("24", 24)]
    [InlineData("-0x8", -8)]
    public void ParseOffset(string s, long expected) => Assert.Equal(expected, OffsetsConfig.ParseOffset(s));
}

public class GameMemoryReaderTests
{
    private const long Heap = 0x2_0000_0000;

    /// <summary>Строит фейковый процесс DSR, согласованный с реальным Data/ds1/offsets.json.</summary>
    private static (FakeMemory Mem, OffsetsConfig Cfg) BuildProcess()
    {
        var cfg = OffsetsConfig.Load(Path.Combine(TestData.DataDir, "offsets.json"));
        var mem = new FakeMemory(0x20000);
        mem.AddRegion(Heap, 0x10000);

        long nextInstr = mem.ModuleBase + 0x400;
        long nextStatic = mem.ModuleBase + 0x18000;
        var heapFor = new Dictionary<string, long>();
        int i = 0;
        foreach (var (name, def) in cfg.Bases)
        {
            var pattern = AobPattern.Parse(def.Aob);
            var bytes = pattern.Bytes.Select((b, j) => pattern.Mask[j] ? b : (byte)0x00).ToArray();
            mem.Write(nextInstr, bytes);
            mem.WriteInt32(nextInstr + def.RipOffset, (int)(nextStatic - (nextInstr + def.InstructionLength)));
            var obj = Heap + 0x1000L * (i + 1);
            mem.WriteInt64(nextStatic, obj);
            heapFor[name] = obj;
            nextInstr += 0x200; nextStatic += 0x10; i++;
        }

        // GameDataMan → PlayerGameData
        long gdm = heapFor["game_data_man"], pgd = Heap + 0x8000;
        mem.WriteInt64(gdm + 0x10, pgd);
        void Field(string f, int v)
        {
            var offs = cfg.Fields[f].ParsedOffsets();
            var b = heapFor[cfg.Fields[f].Base];
            var addr = offs.Count == 1 ? b + offs[0] : (cfg.Fields[f].Base == "game_data_man" ? pgd : ReadPtr(mem, b + offs[0])) + offs[^1];
            if (cfg.Fields[f].Type == FieldType.Byte) mem.Write(addr, new[] { (byte)v });
            else mem.WriteInt32(addr, v);
        }

        long wcmPlayer = Heap + 0x6000;
        mem.WriteInt64(heapFor["world_chr_man"] + 0x68, wcmPlayer);

        Field("deaths", 42); Field("souls", 12345); Field("level", 30);
        Field("vitality", 25); Field("attunement", 12); Field("endurance", 30); Field("strength", 22);
        Field("dexterity", 16); Field("intelligence", 8); Field("faith", 9); Field("resistance", 11);
        Field("weapon_id", 301005); Field("weapon_durability", 50); Field("weapon_max_durability", 400);
        Field("area", 15); Field("block", 1); Field("bloodstain_souls", 777); Field("boss_fight_active", 1);
        mem.Write(pgd + 0xA8, Encoding.Unicode.GetBytes("Солер\0"));

        // Инвентарь: [[PGD]+0x418]+0 → массив, [PGD+0x410] → количество.
        long list = Heap + 0xA000;
        mem.WriteInt64(pgd + 0x418, list);
        mem.WriteInt32(pgd + 0x410, 3);
        void Entry(int idx, uint category, int id, int qty)
        {
            var e = list + idx * cfg.Inventory!.EntrySize;
            mem.WriteInt32(e + cfg.Inventory.CategoryOffset, unchecked((int)category));
            mem.WriteInt32(e + cfg.Inventory.IdOffset, id);
            mem.WriteInt32(e + cfg.Inventory.QuantityOffset, qty);
        }
        Entry(0, 0x40000000, 200, 5);      // Эстус
        Entry(1, 0x00000000, 301005, 1);   // Клеймор +5
        Entry(2, 0x20000000, 100, 1);      // Кольцо Хавела
        return (mem, cfg);
    }

    private static long ReadPtr(FakeMemory m, long a) => m.ReadInt64(a) ?? throw new InvalidOperationException();

    [Fact]
    public void ReadsFullSnapshot_FromFakeProcess()
    {
        var (mem, cfg) = BuildProcess();
        using var reader = new GameMemoryReader(TestData.Knowledge.Game, cfg, () => mem);
        var s = reader.Read();

        Assert.True(s.IsConnected);
        Assert.Equal(cfg.Bases.Count, reader.ResolvedBases.Count);
        Assert.Equal("Солер", s.CharacterName);
        Assert.Equal(42, s.Deaths);
        Assert.Equal(12345, s.Souls);
        Assert.Equal(30, s.Level);
        Assert.Equal(25, s.Stats!["vitality"]);
        Assert.Equal(11, s.Stats["resistance"]);
        Assert.Equal(GameKey.DarkSouls1, s.Game);
        Assert.Equal(5, s.Weapon!.UpgradeLevel);
        Assert.Equal(0.125, s.Weapon.DurabilityRatio);
        Assert.Equal((15, 1), (s.Area, s.Block));
        Assert.Equal(777, s.BloodstainSouls);
        Assert.True(s.InBossFight);
        Assert.Equal(3, s.Inventory!.Count);
        Assert.Contains(new InventoryEntry(ItemKind.Ring, 100, 1), s.Inventory);
        Assert.Equal("anor_londo", TestData.Knowledge.ResolveLocation(s)!.Id);
    }

    [Fact]
    public void ImplausibleValues_AreDiscarded()
    {
        var (mem, cfg) = BuildProcess();
        long pgd = Heap + 0x8000;
        mem.WriteInt32(pgd + 0x90, 5000);   // уровень > 713 — неверное смещение
        mem.WriteInt32(pgd + 0x40, -3);     // живучесть < 1
        using var reader = new GameMemoryReader(TestData.Knowledge.Game, cfg, () => mem);
        var s = reader.Read();
        Assert.Null(s.Level);
        Assert.False(s.Stats!.Values.ContainsKey("vitality")); // только неверное поле отбрасывается
        Assert.Equal(22, s.Stats["strength"]);
        Assert.Equal(12345, s.Souls);
    }

    [Fact]
    public void NoProcess_ReturnsDisconnected_AndRetriesLater()
    {
        var attempts = 0;
        using var reader = new GameMemoryReader(TestData.Knowledge.Game, new OffsetsConfig(), () => { attempts++; return null; });
        Assert.False(reader.Read().IsConnected);
        Assert.False(reader.Read().IsConnected); // в пределах 2 секунд повторно не открываем процесс
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ProcessExit_Disconnects()
    {
        var (mem, cfg) = BuildProcess();
        using var reader = new GameMemoryReader(TestData.Knowledge.Game, cfg, () => mem);
        Assert.True(reader.Read().IsConnected);
        mem.IsAlive = false;
        Assert.False(reader.Read().IsConnected);
    }

    [Fact]
    public void MemoryAccess_IsStrictlyReadOnly()
    {
        // Только PROCESS_VM_READ | PROCESS_QUERY_INFORMATION.
        Assert.Equal(0x0010u | 0x0400u, WindowsProcessMemory.AccessRights);

        // В сборке Core нет ни одного P/Invoke, способного писать в чужую память.
        var forbidden = new[] { "WriteProcessMemory", "VirtualProtectEx", "VirtualAllocEx", "CreateRemoteThread", "NtWriteVirtualMemory" };
        var imports = typeof(WindowsProcessMemory).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<DllImportAttribute>() is not null)
            .Select(m => m.GetCustomAttribute<DllImportAttribute>()!.EntryPoint ?? m.Name)
            .ToList();
        Assert.Contains("ReadProcessMemory", imports);
        Assert.DoesNotContain(imports, name => forbidden.Contains(name, StringComparer.OrdinalIgnoreCase));
    }
}
