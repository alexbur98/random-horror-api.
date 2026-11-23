using DS1Companion.Core.Game;
using DS1Companion.Core.Models;

namespace DS1Companion.Tests;

public class GameSessionTrackerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0);
    private readonly GameSessionTracker _tracker = new(TestData.Knowledge);

    private static GameSnapshot Snap(int deaths = 10, int souls = 1000, string loc = "undead_burg",
        IReadOnlyList<InventoryEntry>? inv = null, EquippedWeapon? weapon = null, bool boss = false, string? bossId = null,
        int? stain = null, string name = "Hero") => new()
    {
        IsConnected = true, CharacterName = name, Deaths = deaths, Souls = souls, Level = 20, LocationId = loc,
        Inventory = inv ?? Array.Empty<InventoryEntry>(), Weapon = weapon, InBossFight = boss, BossId = bossId, BloodstainSouls = stain,
    };

    [Fact]
    public void FirstSnapshot_IsBaseline_NoPickups()
    {
        var picks = new List<ItemPickup>();
        _tracker.ItemPickedUp += picks.Add;
        _tracker.Process(Snap(inv: new[] { new InventoryEntry(ItemKind.Goods, 200, 5) }), T0);
        Assert.Empty(picks);
    }

    [Fact]
    public void NewInventoryEntry_RaisesPickup_WithKnowledgeBaseItem()
    {
        var picks = new List<ItemPickup>();
        _tracker.ItemPickedUp += picks.Add;
        _tracker.Process(Snap(), T0);
        _tracker.Process(Snap(inv: new[] { new InventoryEntry(ItemKind.Goods, 1000, 3) }), T0.AddSeconds(1));

        var p = Assert.Single(picks);
        Assert.Equal("titanite_shard", p.Item.Id);
        Assert.Equal(3, p.Quantity);
        Assert.Equal(1, _tracker.ItemsPickedThisSession);
    }

    [Fact]
    public void Reconnect_DoesNotReportWholeInventoryAsNew()
    {
        var picks = new List<ItemPickup>();
        _tracker.ItemPickedUp += picks.Add;
        var inv = new[] { new InventoryEntry(ItemKind.Goods, 1000, 3) };
        _tracker.Process(Snap(inv: inv), T0);
        _tracker.Process(GameSnapshot.Disconnected, T0.AddSeconds(1));
        _tracker.Process(Snap(inv: inv), T0.AddSeconds(2));
        Assert.Empty(picks);
    }

    [Fact]
    public void Death_WithSouls_CreatesBloodstain_Heuristic_AndRecovers()
    {
        var died = new List<int>();
        var recovered = new List<int>();
        _tracker.Died += died.Add;
        _tracker.BloodstainRecovered += recovered.Add;

        _tracker.Process(Snap(deaths: 10, souls: 4210), T0);
        _tracker.Process(Snap(deaths: 11, souls: 0), T0.AddSeconds(1));
        Assert.Equal(new[] { 4210 }, died);
        Assert.Equal(4210, _tracker.BloodstainSouls);
        Assert.Equal(1, _tracker.SessionDeaths);

        _tracker.Process(Snap(deaths: 11, souls: 4300), T0.AddSeconds(2)); // подобрали пятно (+ немного душ)
        Assert.Equal(new[] { 4210 }, recovered);
        Assert.Equal(0, _tracker.BloodstainSouls);
    }

    [Fact]
    public void SecondDeath_ReplacesOldBloodstain()
    {
        _tracker.Process(Snap(deaths: 0, souls: 5000), T0);
        _tracker.Process(Snap(deaths: 1, souls: 0), T0.AddSeconds(1));
        _tracker.Process(Snap(deaths: 1, souls: 300), T0.AddSeconds(2));
        _tracker.Process(Snap(deaths: 2, souls: 0), T0.AddSeconds(3));
        Assert.Equal(300, _tracker.BloodstainSouls);
    }

    [Fact]
    public void DirectBloodstainValue_FromMemory_WinsOverHeuristic()
    {
        _tracker.Process(Snap(deaths: 0, souls: 999, stain: 0), T0);
        _tracker.Process(Snap(deaths: 1, souls: 0, stain: 777), T0.AddSeconds(1));
        Assert.Equal(777, _tracker.BloodstainSouls);
    }

    [Fact]
    public void LowDurability_WarnsOnce_UntilRepaired()
    {
        var warnings = 0;
        _tracker.WeaponDurabilityLow += _ => warnings++;
        EquippedWeapon W(int d) => new() { RawId = 301005, Durability = d, MaxDurability = 100 };

        _tracker.Process(Snap(weapon: W(100)), T0);
        _tracker.Process(Snap(weapon: W(50)), T0);
        _tracker.Process(Snap(weapon: W(29)), T0);
        _tracker.Process(Snap(weapon: W(10)), T0);
        Assert.Equal(1, warnings);

        _tracker.Process(Snap(weapon: W(100)), T0); // починили
        _tracker.Process(Snap(weapon: W(20)), T0);
        Assert.Equal(2, warnings);
    }

    [Fact]
    public void BossFight_Victory_And_Defeat()
    {
        var results = new List<BossFightResult>();
        var started = new List<BossInfo>();
        _tracker.BossFightStarted += started.Add;
        _tracker.BossFightEnded += results.Add;

        _tracker.Process(Snap(deaths: 5), T0);
        _tracker.Process(Snap(deaths: 5, boss: true), T0.AddSeconds(1));      // босс локации (Таурус-демон)
        _tracker.Process(Snap(deaths: 6, boss: false), T0.AddSeconds(61));    // умерли
        _tracker.Process(Snap(deaths: 6, boss: true, bossId: "taurus_demon"), T0.AddSeconds(100));
        _tracker.Process(Snap(deaths: 6, boss: false), T0.AddSeconds(190));   // победили

        Assert.Equal(2, started.Count);
        Assert.Equal("taurus_demon", started[0].Id);
        Assert.False(results[0].Victory);
        Assert.Equal(TimeSpan.FromSeconds(60), results[0].Duration);
        Assert.True(results[1].Victory);
        Assert.Equal(TimeSpan.FromSeconds(90), results[1].Duration);
    }

    [Fact]
    public void LocationChange_IsReported_AndCharacterSwitchResets()
    {
        var locations = new List<string?>();
        var characters = new List<string>();
        _tracker.LocationChanged += l => locations.Add(l?.Id);
        _tracker.CharacterChanged += characters.Add;

        _tracker.Process(Snap(loc: "undead_burg"), T0);
        _tracker.Process(Snap(loc: "undead_burg"), T0);
        _tracker.Process(Snap(loc: "depths"), T0);
        _tracker.Process(Snap(loc: "depths", name: "Другой", deaths: 99), T0);

        // после смены персонажа сессия сбрасывается, и локация объявляется заново
        Assert.Equal(new[] { "undead_burg", "depths", "depths" }, locations);
        Assert.Equal(new[] { "Hero", "Другой" }, characters);
        Assert.Equal(0, _tracker.SessionDeaths);
    }

    [Fact]
    public void AreaAndBlock_FromMemory_ResolveLocation()
    {
        _tracker.Process(Snap() with { LocationId = null, Area = 15, Block = 1 }, T0);
        Assert.Equal("anor_londo", _tracker.CurrentLocation?.Id);
    }
}
