using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DS1Companion.Core.Game;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Presentation;

/// <summary>Компактная панель тестирования демо-режима.</summary>
public sealed partial class DemoPanelViewModel : ObservableObject
{
    private readonly MockGameStateReader _mock;

    [ObservableProperty] private string _lastAction = "Демо-режим: нажимайте кнопки, чтобы симулировать игру.";
    [ObservableProperty] private bool _isExpanded = true;

    public DemoPanelViewModel(MockGameStateReader mock) => _mock = mock;

    private void Report(ItemInfo? item, string fallback) =>
        LastAction = item is null ? fallback : $"Подобран: {item.NameRu}";

    [RelayCommand] private void PickCommon() => Report(_mock.PickUpRandom(Rarity.Common), "Нет обычных предметов в базе");
    [RelayCommand] private void PickUncommon() => Report(_mock.PickUpRandom(Rarity.Uncommon), "Нет необычных предметов в базе");
    [RelayCommand] private void PickRare() => Report(_mock.PickUpRandom(Rarity.Rare), "Нет редких предметов в базе");
    [RelayCommand] private void PickVeryRare() => Report(_mock.PickUpRandom(Rarity.VeryRare), "Нет очень редких предметов в базе");
    [RelayCommand] private void PickLegendary() => Report(_mock.PickUp("balder_side_sword"), "Нет Балдерского прямого меча в базе");
    [RelayCommand] private void PickKey() => Report(_mock.PickUp("basement_key"), "Нет ключа в базе");

    [RelayCommand] private void AddSouls() { _mock.AddSouls(2_500); LastAction = "+2 500 душ"; }
    [RelayCommand] private void LevelUp() { _mock.LevelUp(); LastAction = "Попытка повысить уровень у Хранительницы огня"; }
    [RelayCommand] private void Die() { _mock.Die(); LastAction = "ВЫ ПОГИБЛИ — души остались в кровавом пятне"; }
    [RelayCommand] private void Recover() { _mock.RecoverBloodstain(); LastAction = "Души из кровавого пятна возвращены"; }
    [RelayCommand] private void DamageWeapon() { _mock.DamageWeapon(); LastAction = "Оружие повреждено на 25%"; }
    [RelayCommand] private void RepairWeapon() { _mock.RepairWeapon(); LastAction = "Оружие починено"; }
    [RelayCommand] private void UpgradeWeapon() { _mock.UpgradeWeapon(); LastAction = "Оружие улучшено на +1"; }
    [RelayCommand] private void NextLocation() { var l = _mock.NextLocation(); LastAction = l is null ? "Нет локаций" : $"Локация: {l.NameRu}"; }
    [RelayCommand] private void StartBoss() { var b = _mock.StartBossFight(); LastAction = b is null ? "Нет боссов" : $"Бой с боссом: {b.NameRu}"; }
    [RelayCommand] private void WinBoss() { _mock.EndBossFight(true); LastAction = "Босс повержен!"; }
    [RelayCommand] private void LoseBoss() { _mock.EndBossFight(false); LastAction = "Поражение в бою с боссом"; }
    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;
}
