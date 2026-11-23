using DS1Companion.Core.Game;
using DS1Companion.Core.Models;
using DS1Companion.Core.Presentation;

namespace DS1Companion.Tests;

public class MainViewModelTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(100);

    private static void Run(MainViewModel vm, TimeSpan total)
    {
        for (var t = TimeSpan.Zero; t < total; t += Frame) vm.Tick(Frame);
    }

    [Fact]
    public void StartsInDemo_AndPopulatesDashboard()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir);
        vm.Poll();

        Assert.True(vm.IsDemoMode);
        Assert.Equal(ConnectionState.Demo, vm.ConnectionState);
        Assert.Equal("Демо · DS1", vm.ConnectionLabel);
        Assert.Equal("Город Нежити", vm.LocationNameRu);
        Assert.Equal(37, vm.TotalDeaths);
        Assert.Equal(0, vm.SessionDeaths);
        Assert.Equal("Клеймор +5", vm.Weapon.Title);
        Assert.InRange(vm.Tips.Count, 1, 2);
        Assert.InRange(vm.SoulsProgress, 0.01, 1);
        Assert.NotEmpty(vm.StatRows);
    }

    [Fact]
    public void LegendaryPickup_ShowsCard_PlaysChord_ThenMovesToRecentAfterTenSeconds()
    {
        using var dir = new TempDir();
        var (vm, sound, _, _, _) = VmFactory.Create(dir);
        vm.Poll();
        vm.Demo!.PickLegendaryCommand.Execute(null);
        vm.Poll();

        Assert.True(vm.IsCardVisible);
        var card = vm.CurrentCard!;
        Assert.Equal("Балдерский прямой меч", card.Name);
        Assert.Equal("ЛЕГЕНДАРНЫЙ", card.RarityLabel);
        Assert.Equal("НЕ ВЫБРАСЫВАТЬ", card.KeepTag);
        Assert.True(card.IsLegendary);
        Assert.Equal(new[] { SoundCue.LegendaryChord }, sound.Played);
        Assert.Empty(vm.RecentLoot);

        Run(vm, TimeSpan.FromSeconds(9.8));
        Assert.True(vm.IsCardVisible);
        Run(vm, TimeSpan.FromSeconds(0.3));
        Assert.False(vm.IsCardVisible);
        Assert.Equal("balder_side_sword", Assert.Single(vm.RecentLoot).Item.Id);
        Assert.Contains(vm.Profile!.RareCollection.Keys, k => k == "balder_side_sword");
    }

    [Fact]
    public void HoverPausesCardCountdown()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir);
        vm.Poll();
        vm.Demo!.PickRareCommand.Execute(null);
        vm.Poll();

        Run(vm, TimeSpan.FromSeconds(5));
        vm.CurrentCard!.IsHovered = true;
        Run(vm, TimeSpan.FromSeconds(60));
        Assert.True(vm.IsCardVisible);
        Assert.Equal(0.5, vm.CurrentCard.RemainingFraction, 1);

        vm.CurrentCard.IsHovered = false;
        Run(vm, TimeSpan.FromSeconds(5.2));
        Assert.False(vm.IsCardVisible);
    }

    [Fact]
    public void MultiplePickups_AreQueued_DismissShowsNext()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir);
        vm.Poll();
        vm.Demo!.PickKeyCommand.Execute(null);
        vm.Demo.PickLegendaryCommand.Execute(null);
        vm.Poll();

        var first = vm.CurrentCard!;
        Assert.Contains("Ещё 1", vm.PendingCardsText);
        vm.DismissCardCommand.Execute(null);
        Assert.NotSame(first, vm.CurrentCard);
        Assert.NotNull(vm.CurrentCard);
        Assert.Equal("", vm.PendingCardsText);
        Assert.Single(vm.RecentLoot);
        vm.DismissCardCommand.Execute(null);
        Assert.Null(vm.CurrentCard);
        Assert.Equal(2, vm.RecentLoot.Count);
    }

    [Fact]
    public void RecentLoot_IsCapped_AndClickReopensCardWithoutDuplicating()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir, minRarity: Rarity.Legendary); // карточки только для легендарных/ключей
        vm.Poll();
        var mock = (MockGameStateReader)vm.CurrentReader;
        for (int i = 0; i < 6; i++)
        {
            mock.PickUp("soul_lost_undead");
            vm.Poll();
        }
        Assert.False(vm.IsCardVisible);
        Assert.Equal(MainViewModel.MaxRecentLoot, vm.RecentLoot.Count);

        var row = vm.RecentLoot[2];
        vm.OpenLootCommand.Execute(row);
        Assert.Same(row.Item, vm.CurrentCard!.Item);
        Assert.True(vm.CurrentCard.IsReplay);
        vm.DismissCardCommand.Execute(null);
        Assert.Equal(MainViewModel.MaxRecentLoot, vm.RecentLoot.Count);
        Assert.Same(row, vm.RecentLoot[2]);
    }

    [Fact]
    public void Death_ShowsBloodstain_RecoverHidesIt()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir);
        vm.Poll();
        vm.Demo!.DieCommand.Execute(null);
        vm.Poll();
        Assert.True(vm.HasBloodstain);
        Assert.Contains("4 210", vm.BloodstainText.Replace(' ', ' '));
        Assert.Equal(1, vm.SessionDeaths);
        Assert.Equal(38, vm.TotalDeaths);

        vm.Demo.RecoverCommand.Execute(null);
        vm.Poll();
        Assert.False(vm.HasBloodstain);
    }

    [Fact]
    public void LowDurability_ShowsWarning_AndPlaysSoftSignalOnce()
    {
        using var dir = new TempDir();
        var (vm, sound, _, _, _) = VmFactory.Create(dir);
        vm.Poll();
        for (int i = 0; i < 3; i++)
        {
            vm.Demo!.DamageWeaponCommand.Execute(null);
            vm.Poll();
        }
        Assert.True(vm.ShowDurabilityWarning);
        Assert.Equal(new[] { SoundCue.DurabilityWarning }, sound.Played);
        vm.Demo!.RepairWeaponCommand.Execute(null);
        vm.Poll();
        Assert.False(vm.ShowDurabilityWarning);
    }

    [Fact]
    public void BossFight_IsRecordedInProfile_AndPersisted()
    {
        using var dir = new TempDir();
        var (vm, _, clock, _, profiles) = VmFactory.Create(dir);
        vm.Poll();
        vm.Demo!.StartBossCommand.Execute(null);
        vm.Poll();
        clock.Advance(TimeSpan.FromSeconds(75));
        vm.Demo.LoseBossCommand.Execute(null);
        vm.Poll();
        vm.Demo.StartBossCommand.Execute(null);
        vm.Poll();
        clock.Advance(TimeSpan.FromSeconds(120));
        vm.Demo.WinBossCommand.Execute(null);
        vm.Poll();

        var row = vm.BossStats.First();
        Assert.Equal("Таурус-демон", row.Name);
        Assert.Equal(2, row.Attempts);
        Assert.True(row.Defeated);

        var saved = profiles.Load(GameKey.DarkSouls1, vm.Profile!.CharacterName).Boss("taurus_demon");
        Assert.Equal(2, saved.Attempts);
        Assert.Equal(1, saved.Defeats);
        Assert.Equal(120, saved.BestVictorySeconds);
    }

    [Fact]
    public void ToggleDemo_SwitchesToLiveReader_AndIgnoresStaleSnapshots()
    {
        using var dir = new TempDir();
        var (vm, _, _, settings, _) = VmFactory.Create(dir);
        var demoReader = vm.CurrentReader;
        vm.ToggleDemoCommand.Execute(null);

        Assert.False(vm.IsDemoMode);
        Assert.Null(vm.Demo);
        Assert.Equal(ConnectionState.Waiting, vm.ConnectionState);
        Assert.Equal("Ожидание игры", vm.ConnectionLabel);
        Assert.False(settings.StartInDemo);

        vm.OnSnapshot(demoReader, new GameSnapshot { IsConnected = true, Deaths = 999 });
        Assert.NotEqual(999, vm.TotalDeaths);
        vm.Poll();
        Assert.Equal(ConnectionState.Waiting, vm.ConnectionState);
    }

    [Fact]
    public void SessionTimer_RunsOnlyWhileConnectedOrDemo()
    {
        using var dir = new TempDir();
        var (vm, _, _, _, _) = VmFactory.Create(dir, demo: false);
        vm.Poll();
        Run(vm, TimeSpan.FromSeconds(3));
        Assert.Equal("00:00:00", vm.SessionTime);

        vm.ToggleDemoCommand.Execute(null);
        vm.Poll();
        Run(vm, TimeSpan.FromSeconds(3725));
        Assert.Equal("01:02:05", vm.SessionTime);
    }

    [Fact]
    public void VolumeAndMute_AreAppliedAndSavedWithDebounce()
    {
        using var dir = new TempDir();
        var (vm, sound, _, settings, _) = VmFactory.Create(dir);
        vm.Volume = 0.25;
        vm.IsMuted = true;
        Assert.Equal(0.25, sound.Volume);
        Assert.True(sound.IsMuted);
        Assert.False(File.Exists(Path.Combine(dir.Path, "settings.json")));
        Run(vm, TimeSpan.FromSeconds(1.2));
        Assert.True(File.Exists(Path.Combine(dir.Path, "settings.json")));
        Assert.Equal(0.25, settings.Volume);
    }

    [Fact]
    public void ItemCard_HoverAndPinPauseIndependently()
    {
        var card = new ItemCardViewModel(TestData.Item("x"), DS1Companion.Core.Services.IconSource.ForGlyph(IconGlyph.Flask), 1,
            DateTime.Now, TimeSpan.FromSeconds(10));
        card.IsPinned = true;
        card.IsHovered = true;
        card.IsHovered = false;
        Assert.False(card.Advance(TimeSpan.FromSeconds(30)));
        card.IsPinned = false;
        Assert.True(card.Advance(TimeSpan.FromSeconds(10)));
    }
}
