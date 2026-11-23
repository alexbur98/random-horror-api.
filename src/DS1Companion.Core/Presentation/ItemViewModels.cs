using CommunityToolkit.Mvvm.ComponentModel;
using DS1Companion.Core.Logic;
using DS1Companion.Core.Models;
using DS1Companion.Core.Services;

namespace DS1Companion.Core.Presentation;

/// <summary>Большая карточка подбора предмета (состояние 2).</summary>
public sealed partial class ItemCardViewModel : ObservableObject
{
    public ItemInfo Item { get; }
    public PopupCountdown Countdown { get; }
    public int Quantity { get; }
    public DateTime PickedAt { get; }
    /// <summary>true — карточка открыта кликом по «недавним», а не новым подбором.</summary>
    public bool IsReplay { get; }

    public string Name => Item.NameRu;
    public string NameEn => Item.NameEn;
    public Rarity Rarity => Item.Rarity;
    public string RarityLabel => DisplayText.RarityLabelRu(Item.Rarity);
    public string RarityColor => DisplayText.RarityColorHex(Item.Rarity);
    public string CategoryLabel => DisplayText.CategoryLabelRu(Item.Category);
    public string RarityReason => Item.RarityReason;
    public string Summary => Item.SummaryRu;
    public string KeepTag => DisplayText.KeepTagRu(Item.KeepStatus, Game);
    public GameDefinition? Game { get; init; }
    public bool IsLegendary => Item.Rarity == Rarity.Legendary;
    public bool IsHighRarity => Item.Rarity >= Rarity.VeryRare;
    public string QuantityText => Quantity > 1 ? $"× {Quantity}" : "";
    public bool HasQuantity => Quantity > 1;

    [ObservableProperty] private IconSource _icon;
    [ObservableProperty] private double _remainingFraction = 1.0;
    [ObservableProperty] private bool _isHovered;
    [ObservableProperty] private bool _isPinned;

    public ItemCardViewModel(ItemInfo item, IconSource icon, int quantity, DateTime pickedAt, TimeSpan duration, bool isReplay = false)
    {
        Item = item;
        _icon = icon;
        Quantity = quantity;
        PickedAt = pickedAt;
        IsReplay = isReplay;
        Countdown = new PopupCountdown(duration);
    }

    partial void OnIsHoveredChanged(bool value) => UpdatePause();
    partial void OnIsPinnedChanged(bool value) => UpdatePause();

    private void UpdatePause()
    {
        if (IsHovered || IsPinned) Countdown.Pause();
        else Countdown.Resume();
    }

    /// <summary>Сдвигает таймер; true — карточка истекла на этом шаге.</summary>
    public bool Advance(TimeSpan delta)
    {
        var expired = Countdown.Advance(delta);
        RemainingFraction = Countdown.RemainingFraction;
        return expired;
    }
}

/// <summary>Строка «недавнего лута» в нижней части спокойного экрана.</summary>
public sealed partial class LootRowViewModel : ObservableObject
{
    public ItemInfo Item { get; }
    public int Quantity { get; }
    public DateTime PickedAt { get; }

    public string Name => Item.NameRu;
    public string RarityColor => DisplayText.RarityColorHex(Item.Rarity);
    public string RarityLabel => DisplayText.RarityLabelRu(Item.Rarity);
    public string CategoryLabel => DisplayText.CategoryLabelRu(Item.Category);
    public string TimeText => PickedAt.ToString("HH:mm");
    public string QuantityText => Quantity > 1 ? $"× {Quantity}" : "";

    [ObservableProperty] private IconSource _icon;

    public LootRowViewModel(ItemInfo item, IconSource icon, int quantity, DateTime pickedAt)
    {
        Item = item;
        _icon = icon;
        Quantity = quantity;
        PickedAt = pickedAt;
    }
}

public sealed partial class WeaponPanelViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasWeapon;
    [ObservableProperty] private string _title = "Оружие не экипировано";
    [ObservableProperty] private string _scalingLetters = "";
    [ObservableProperty] private string _scalingHint = "";
    [ObservableProperty] private string _nextStep = "";
    [ObservableProperty] private bool _hasNextStep;
    [ObservableProperty] private double _durabilityFraction = 1.0;
    [ObservableProperty] private string _durabilityText = "";
    [ObservableProperty] private bool _hasDurability;

    public void Update(EquippedWeapon? weapon, WeaponAdvice? advice)
    {
        HasWeapon = weapon is not null;
        if (weapon is null || advice is null)
        {
            Title = "Оружие не экипировано";
            ScalingLetters = ScalingHint = NextStep = DurabilityText = "";
            HasNextStep = false;
            HasDurability = false;
            DurabilityFraction = 1;
            return;
        }
        Title = advice.DisplayName;
        ScalingLetters = advice.ScalingLetters;
        ScalingHint = advice.ScalingHint;
        NextStep = advice.NextStep;
        HasNextStep = !string.IsNullOrEmpty(advice.NextStep);
        DurabilityFraction = weapon.DurabilityRatio;
        HasDurability = weapon.MaxDurability > 0;
        DurabilityText = weapon.MaxDurability > 0 ? $"Прочность {weapon.Durability}/{weapon.MaxDurability}" : "";
    }
}

public sealed record StatRowViewModel(string Name, int Value, string Hint, StatAdvice Advice)
{
    public bool IsWarning => Advice == StatAdvice.Warning;
    public bool IsCapped => Advice is StatAdvice.Capped or StatAdvice.Efficient;
}

public sealed record CollectedRowViewModel(string Name, string RarityColor, string FoundText);

public sealed record BossStatRowViewModel(string Name, string NameEn, int Attempts, string TimeText, bool Defeated, string StatusText)
{
    public bool HasAttempts => Attempts > 0;
}
