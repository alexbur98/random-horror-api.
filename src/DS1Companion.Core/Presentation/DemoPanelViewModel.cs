using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DS1Companion.Core.Game;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Presentation;

/// <summary>Компактная панель тестирования демо-режима. Подписи зависят от игры.</summary>
public sealed partial class DemoPanelViewModel : ObservableObject
{
    private readonly MockGameStateReader _mock;
    private readonly GameDefinition _game;

    [ObservableProperty] private string _lastAction;
    [ObservableProperty] private bool _isExpanded = true;

    public string LegendaryLabel { get; }
    public string AddSoulsLabel => $"+2 500 {_game.CurrencyGenitiveRu}";
    public bool HasLevels => _game.HasLevels;
    public bool HasBloodstain => _game.HasBloodstain;
    public bool HasHollowing => _game.HasHollowing;
    public bool HasDurability => _game.HasDurability;
    public bool HasWeaponUpgrades => _game.HasWeaponUpgrades;
    public string RecoverLabel => _game.HasBloodstain ? "Вернуть " + _game.CurrencyGenitiveRu : "";

    public DemoPanelViewModel(MockGameStateReader mock, string? legendaryName)
    {
        _mock = mock;
        _game = mock.Game;
        LegendaryLabel = legendaryName ?? "Легендарный";
        _lastAction = $"Демо: {_game.TitleRu}. Нажимайте кнопки, чтобы симулировать игру.";
    }

    private void Report(ItemInfo? item, string fallback) =>
        LastAction = item is null ? fallback : $"Подобран: {item.NameRu}";

    [RelayCommand] private void PickCommon() => Report(_mock.PickUpRandom(Rarity.Common), "Нет обычных предметов в базе");
    [RelayCommand] private void PickUncommon() => Report(_mock.PickUpRandom(Rarity.Uncommon), "Нет необычных предметов в базе");
    [RelayCommand] private void PickRare() => Report(_mock.PickUpRandom(Rarity.Rare), "Нет редких предметов в базе");
    [RelayCommand] private void PickVeryRare() => Report(_mock.PickUpRandom(Rarity.VeryRare), "Нет очень редких предметов в базе");
    [RelayCommand] private void PickLegendary() => Report(_mock.PickUpLegendary(), "Нет легендарных предметов в базе");
    [RelayCommand] private void PickKey() => Report(_mock.PickUpKey(), "Нет ключевых предметов в базе");

    [RelayCommand] private void AddSouls() { _mock.AddSouls(2_500); LastAction = AddSoulsLabel; }
    [RelayCommand] private void LevelUp() { _mock.LevelUp(); LastAction = "Попытка повысить уровень"; }
    [RelayCommand] private void Die()
    {
        _mock.Die();
        LastAction = _game.HasBloodstain ? $"Смерть — {_game.CurrencyRu.ToLowerInvariant()} остались на месте гибели" : "Смерть";
    }
    [RelayCommand] private void Recover() { _mock.RecoverBloodstain(); LastAction = "Потерянное возвращено"; }
    [RelayCommand] private void RestoreHumanity() { _mock.RestoreHumanity(); LastAction = "Облик восстановлен — максимальное здоровье снова полное"; }
    [RelayCommand] private void DamageWeapon() { _mock.DamageWeapon(); LastAction = "Оружие повреждено на 25%"; }
    [RelayCommand] private void RepairWeapon() { _mock.RepairWeapon(); LastAction = "Оружие починено"; }
    [RelayCommand] private void UpgradeWeapon() { _mock.UpgradeWeapon(); LastAction = "Оружие улучшено на +1"; }
    [RelayCommand] private void NextLocation() { var l = _mock.NextLocation(); LastAction = l is null ? "Нет локаций" : $"Локация: {l.NameRu}"; }
    [RelayCommand] private void StartBoss() { var b = _mock.StartBossFight(); LastAction = b is null ? "Нет боссов" : $"Бой с боссом: {b.NameRu}"; }
    [RelayCommand] private void WinBoss() { _mock.EndBossFight(true); LastAction = "Босс повержен!"; }
    [RelayCommand] private void LoseBoss() { _mock.EndBossFight(false); LastAction = "Поражение в бою с боссом"; }
    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
