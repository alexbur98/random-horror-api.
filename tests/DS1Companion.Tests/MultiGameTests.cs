using DS1Companion.Core.Data;
using DS1Companion.Core.Game;
using DS1Companion.Core.Logic;
using DS1Companion.Core.Memory;
using DS1Companion.Core.Models;
using DS1Companion.Core.Presentation;

namespace DS1Companion.Tests;

/// <summary>Общие проверки данных — для каждой игры из Data/*/game.json.</summary>
public class AllGamesDataTests
{
    public static IEnumerable<object[]> Games() => TestData.Catalog.Games.Select(g => new object[] { g.Game.Key });

    [Fact]
    public void Catalog_ContainsAllFiveGames()
    {
        foreach (var key in Enum.GetValues<GameKey>()) Assert.NotNull(TestData.Catalog.Find(key));
        Assert.Equal(GameKey.DarkSouls1, TestData.Catalog.Default.Game.Key);
    }

    [Theory, MemberData(nameof(Games))]
    public void Theme_HasValidColors(GameKey key)
    {
        var theme = TestData.Catalog.Get(key).Game.Theme;
        Assert.NotEmpty(theme.ParticleColors);
        Assert.False(string.IsNullOrWhiteSpace(theme.TitleFont));
        foreach (var c in theme.AllColors())
            Assert.Matches("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", c);
    }

    [Fact]
    public void EveryGame_LooksDifferent()
    {
        var themes = TestData.Catalog.Games.Select(g => g.Game.Theme).ToList();
        Assert.Equal(themes.Count, themes.Select(t => t.Accent.ToUpperInvariant()).Distinct().Count());
        Assert.Equal(themes.Count, themes.Select(t => t.Particles).Distinct().Count());
        Assert.Equal(themes.Count, themes.Select(t => t.NameRu).Distinct().Count());
    }

    [Theory, MemberData(nameof(Games))]
    public void Items_AreComplete(GameKey key)
    {
        var kb = TestData.Catalog.Get(key);
        Assert.InRange(kb.Items.Count, 80, 300);
        foreach (var r in Enum.GetValues<Rarity>()) Assert.Contains(kb.Items, i => i.Rarity == r);
        foreach (var i in kb.Items)
        {
            Assert.Matches("^[a-z0-9_]+$", i.Id);
            Assert.False(string.IsNullOrWhiteSpace(i.NameRu + i.NameEn + i.SummaryRu + i.RarityReason));
            Assert.False(string.IsNullOrWhiteSpace(i.SummaryRu), $"{key}/{i.Id}");
            Assert.DoesNotContain("?", i.SummaryRu + i.RarityReason);
        }
    }

    [Theory, MemberData(nameof(Games))]
    public void GameDefinition_IsConsistent(GameKey key)
    {
        var kb = TestData.Catalog.Get(key);
        var g = kb.Game;
        Assert.NotEmpty(g.ProcessNames);
        Assert.NotEmpty(g.Stats);
        Assert.Equal(g.Stats.Count, g.Stats.Select(s => s.Key).Distinct().Count());
        Assert.NotNull(kb.FindLocation(g.Demo.LocationId!));
        foreach (var id in new[] { g.Demo.WeaponItemId, g.Demo.HealingItemId, g.Demo.LegendaryItemId, g.Demo.KeyItemId }.OfType<string>())
            Assert.NotNull(kb.FindItem(id));
        Assert.Equal(Rarity.Legendary, kb.FindItem(g.Demo.LegendaryItemId!)!.Rarity);
        Assert.All(g.Demo.Stats.Keys, k => Assert.Contains(g.Stats, s => s.Key == k));
        if (g.HasHollowing) Assert.Contains("{0}", g.HollowingTextRu);
        if (g.HasBloodstain) Assert.Contains("{0}", g.BloodstainTextRu);
    }

    [Theory, MemberData(nameof(Games))]
    public void UpgradesLocationsBosses_ReferenceExistingData(GameKey key)
    {
        var kb = TestData.Catalog.Get(key);
        foreach (var w in kb.Upgrades.Weapons)
        {
            Assert.NotNull(kb.FindItem(w.ItemId));
            Assert.NotNull(kb.FindPath(w.PathId));
        }
        foreach (var step in kb.Upgrades.Paths.SelectMany(p => p.Steps))
            Assert.NotNull(kb.FindItem(step.MaterialItemId));
        Assert.True(kb.Locations.Count >= 20);
        Assert.All(kb.Locations, l => Assert.NotEmpty(l.TipsRu));
        Assert.All(kb.Bosses, b => Assert.NotNull(kb.FindLocation(b.LocationId)));
        foreach (var group in kb.Locations.Where(l => l.Area > 0).GroupBy(l => (l.Area, l.Block)))
            Assert.Equal(1, group.Count(l => l.IsPrimaryForMap));
    }

    [Theory, MemberData(nameof(Games))]
    public void Offsets_AreValid_AndCoverAllStats(GameKey key)
    {
        var kb = TestData.Catalog.Get(key);
        var cfg = OffsetsConfig.Load(Path.Combine(kb.Game.Folder, "offsets.json"));
        Assert.Empty(cfg.Validate());
        Assert.Contains(cfg.ProcessName, kb.Game.ProcessNames);
        foreach (var stat in kb.Game.Stats) Assert.True(cfg.Fields.ContainsKey(stat.Key), $"{key}: нет поля {stat.Key}");
        if (kb.Game.LevelCurve == "memory") Assert.True(cfg.Fields.ContainsKey("souls_for_next_level"));
        if (kb.Game.HasHollowing) Assert.True(cfg.Fields.ContainsKey("hollow_level"));
    }
}

public class LevelCurveTests
{
    [Theory]
    [InlineData(1, 673)]
    [InlineData(150, 153680)]
    public void Souls3Formula(int level, int cost) => Assert.Equal(cost, LevelCurve.Souls3Cost(level));

    [Fact]
    public void CurveDependsOnGame()
    {
        Assert.Equal(1039, LevelCurve.CostForNextLevel(TestData.Knowledge.Game, 12));
        Assert.Null(LevelCurve.CostForNextLevel(TestData.Ds2.Game, 12)); // DS2 — из памяти игры
        Assert.Null(LevelCurve.CostForNextLevel(SekiroLike.Game, 12));
    }
}

/// <summary>Условная игра без уровней и без кровавого пятна — проверка того, что ядро не завязано на «души».</summary>
public static class SekiroLike
{
    public static readonly GameDefinition Game = new()
    {
        Key = GameKey.Sekiro, TitleRu = "Sekiro", ShortTitle = "SEK", ProcessNames = { "sekiro" },
        CurrencyRu = "Сэн", CurrencyGenitiveRu = "сэн", HasLevels = false, LevelCurve = "none",
        HasBloodstain = false, HasWeaponUpgrades = false, HasDurability = false,
        Stats = { new StatDefinition { Key = "attack", NameRu = "Сила атаки" } },
        Demo = new DemoProfile { CharacterName = "Волк", Souls = 1234, LocationId = "dilapidated_temple", HealingItemId = "gourd",
            LegendaryItemId = "mortal_blade", Stats = { ["attack"] = 3 } },
    };

    public static KnowledgeBase Knowledge() => new(
        new[]
        {
            new ItemInfo { Id = "gourd", NameRu = "Тыква-горлянка", NameEn = "Healing Gourd", Category = ItemCategory.Consumable, Rarity = Rarity.Uncommon },
            new ItemInfo { Id = "mortal_blade", NameRu = "Смертоносный клинок", NameEn = "Mortal Blade", Category = ItemCategory.KeyItem, Rarity = Rarity.Legendary },
        },
        new WeaponUpgradeData(),
        new[] { new LocationInfo { Id = "dilapidated_temple", NameRu = "Ветхий храм", NameEn = "Dilapidated Temple", TipsRu = { "Скульптор учит навыкам." } } },
        Array.Empty<BossInfo>(),
        Game);
}

public class MultiGameViewModelTests
{
    [Fact]
    public void Ds2Demo_ShowsHollowing_AndMemoryLevelCost()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir, game: GameKey.DarkSouls2);
        vm.Poll();

        Assert.Equal("Dark Souls II: Scholar of the First Sin", vm.GameTitle);
        Assert.Equal("Демо · DS2", vm.ConnectionLabel);
        Assert.Equal("Маджула", vm.LocationNameRu);
        Assert.True(vm.ShowSoulsProgress);
        Assert.Contains("/", vm.SoulsText);
        Assert.Equal(9, vm.StatRows.Count);
        Assert.Equal("Жизненная сила", vm.StatRows[0].Name);
        Assert.False(vm.ShowHollowing);

        vm.Demo!.DieCommand.Execute(null);
        vm.Poll();
        Assert.True(vm.ShowHollowing);
        Assert.Contains("Чучело человека", vm.HollowingText);
        Assert.True(vm.HasBloodstain);

        vm.Demo.RestoreHumanityCommand.Execute(null);
        vm.Poll();
        Assert.False(vm.ShowHollowing);
    }

    [Fact]
    public void Ds2Pickup_WithoutGameIds_IsRecognizedByItemId()
    {
        using var dir = new TempDir();
        var (vm, sound, _, _, _) = VmFactory.Create(dir, game: GameKey.DarkSouls2);
        vm.Poll();
        vm.Demo!.PickLegendaryCommand.Execute(null);
        vm.Poll();
        Assert.Equal("Лунный великий меч", vm.CurrentCard!.Name);
        Assert.Contains(SoundCue.LegendaryChord, sound.Played);

        ((MockGameStateReader)vm.CurrentReader).PickUp("soul_lost_undead");
        vm.DismissCardCommand.Execute(null);
        vm.Poll();
        Assert.Equal("МОЖНО НЕ ХРАНИТЬ", vm.CurrentCard!.KeepTag);
    }

    [Fact]
    public void SelectingAnotherGame_InDemo_SwitchesEverything()
    {
        using var dir = new TempDir();
        var (vm, _, _, settings, _) = VmFactory.Create(dir);
        vm.Poll();
        vm.Demo!.PickCommonCommand.Execute(null);
        vm.Poll();
        vm.DismissCardCommand.Execute(null);
        Assert.NotEmpty(vm.RecentLoot);

        vm.SelectedGame = vm.GameOptions.Single(g => g.Key == GameKey.DarkSouls2);
        Assert.Equal(GameKey.DarkSouls2, vm.Game.Key);
        Assert.Equal(GameKey.DarkSouls2, settings.SelectedGame);
        Assert.IsType<MockGameStateReader>(vm.CurrentReader);
        Assert.Empty(vm.RecentLoot);
        vm.Poll();
        Assert.Equal("Маджула", vm.LocationNameRu);
        Assert.Equal("Демо · DS2", vm.ConnectionLabel);
        Assert.Contains(vm.BossStats, b => b.Name == "Последний гигант");
    }

    [Fact]
    public void LiveSnapshot_FromAnotherGame_AutoSwitches()
    {
        using var dir = new TempDir();
        var fake = new ScriptedReader(new GameSnapshot { IsConnected = true, Game = GameKey.DarkSouls2, CharacterName = "Тестовый", Deaths = 5, Level = 10 });
        var (vm, _, _, _, _) = VmFactory.Create(dir, demo: false, live: () => fake);
        Assert.Equal(GameKey.DarkSouls1, vm.Game.Key);
        vm.Poll();
        Assert.Equal(GameKey.DarkSouls2, vm.Game.Key);
        Assert.Equal(ConnectionState.Connected, vm.ConnectionState);
        Assert.Equal("DS2", vm.ConnectionLabel);
        Assert.Equal(GameKey.DarkSouls2, vm.Profile!.Game);
        Assert.Equal(ParticleStyle.Souls, vm.Theme.Particles); // оформление тоже переключилось
    }

    [Fact]
    public void Sekiro_HasDragonrot_AndNoLevels()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir, game: GameKey.Sekiro);
        vm.Poll();
        Assert.Equal(ParticleStyle.Petals, vm.Theme.Particles);
        Assert.False(vm.ShowSoulsProgress);
        Assert.False(vm.HasWeaponPanel);
        Assert.Equal("Капля драконьей крови", vm.Demo!.HollowingCureLabel);
        vm.Demo.DieCommand.Execute(null);
        vm.Poll();
        Assert.True(vm.ShowHollowing);
        Assert.Equal("ДРАКОНЬЯ ХВОРЬ", vm.HollowingTitle);
        Assert.False(vm.HasBloodstain);
        Assert.Equal(3, vm.StatRows.Count);
    }

    [Fact]
    public void EldenRing_LostRunes_NoDurability_SomberPath()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir, game: GameKey.EldenRing);
        vm.Poll();
        Assert.Equal(ParticleStyle.Grace, vm.Theme.Particles);
        Assert.Contains("Easy Anti-Cheat", vm.WaitingHint);
        Assert.Equal("РУНЫ НА РУКАХ / НА СЛЕДУЮЩИЙ УРОВЕНЬ", vm.CurrencyCaption);
        Assert.False(vm.Weapon.HasDurability);
        Assert.Contains("2 × Кузнечный камень [3]", vm.Weapon.NextStep); // длинный меч +6 → +7: камни [3] открывают +7…+9
        vm.Demo!.DieCommand.Execute(null);
        vm.Poll();
        Assert.Equal("ПОТЕРЯННЫЕ РУНЫ", vm.BloodstainTitle);
        Assert.Contains("рун", vm.BloodstainText);
        Assert.False(vm.ShowDurabilityWarning);
    }

    [Fact]
    public void Ds3_UsesSouls3Curve_AndAshTheme()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir, game: GameKey.DarkSouls3);
        vm.Poll();
        Assert.Equal(ParticleStyle.Ash, vm.Theme.Particles);
        Assert.Contains($"{DisplayText.FormatSouls(LevelCurve.Souls3Cost(35))}", vm.SoulsText);
        Assert.Contains("Андрэ", vm.Weapon.NextStep);
        vm.Demo!.PickLegendaryCommand.Execute(null);
        vm.Poll();
        Assert.Equal("Буреносец", vm.CurrentCard!.Name);
    }

    [Fact]
    public void GameWithoutLevels_ShowsCurrencyOnly_AndNoBloodstain()
    {
        using var dir = new TempDir();
        var catalog = new GameCatalog(new[] { SekiroLike.Knowledge() });
        var (vm, _, _, _, _) = VmFactory.Create(dir, game: GameKey.Sekiro, catalog: catalog);
        vm.Poll();
        Assert.False(vm.ShowSoulsProgress);
        Assert.Equal("1 234 сэн", vm.SoulsText);
        Assert.Equal("СЭН НА РУКАХ", vm.CurrencyCaption);
        Assert.False(vm.HasWeaponPanel);

        vm.Demo!.DieCommand.Execute(null);
        vm.Poll();
        Assert.False(vm.HasBloodstain);
        Assert.Equal("617 сэн", vm.SoulsText); // при смерти теряется половина
        Assert.False(vm.Demo.HasLevels);
    }

    private sealed class ScriptedReader : IGameStateReader
    {
        private readonly GameSnapshot _snapshot;
        public ScriptedReader(GameSnapshot snapshot) => _snapshot = snapshot;
        public string Name => "Тест";
        public GameSnapshot Read() => _snapshot;
        public void Dispose() { }
    }
}

public class MultiGameReaderTests
{
    [Fact]
    public void FindsWhicheverGameIsRunning()
    {
        var mem = new FakeMemory();
        var ds1 = new GameMemoryReader(TestData.Knowledge.Game, new OffsetsConfig(), () => null);
        var ds2 = new GameMemoryReader(TestData.Ds2.Game, new OffsetsConfig { ProcessName = "DarkSoulsII" }, () => mem);
        using var multi = new MultiGameReader(new[] { ds1, ds2 });

        Assert.False(multi.Read().IsConnected); // первый опрос — DS1, не запущена
        var s = multi.Read();                    // второй — DS2
        Assert.True(s.IsConnected);
        Assert.Equal(GameKey.DarkSouls2, s.Game);
        Assert.Equal(GameKey.DarkSouls2, multi.ActiveGame!.Key);
        Assert.True(multi.Read().IsConnected);   // дальше читаем только активную игру

        mem.IsAlive = false;
        Assert.False(multi.Read().IsConnected);
        Assert.Null(multi.ActiveGame);
    }
}
