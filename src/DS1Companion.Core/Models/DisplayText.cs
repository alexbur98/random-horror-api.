namespace DS1Companion.Core.Models;

/// <summary>Цвета и подписи для UI. Держим в Core, чтобы покрыть тестами и не дублировать в XAML.</summary>
public static class DisplayText
{
    public static string RarityColorHex(Rarity r) => r switch
    {
        Rarity.Common => "#9CA3AF",
        Rarity.Uncommon => "#22C55E",
        Rarity.Rare => "#3B82F6",
        Rarity.VeryRare => "#A855F7",
        Rarity.Legendary => "#F59E0B",
        _ => "#9CA3AF",
    };

    public static string RarityLabelRu(Rarity r) => r switch
    {
        Rarity.Common => "ОБЫЧНЫЙ",
        Rarity.Uncommon => "НЕОБЫЧНЫЙ",
        Rarity.Rare => "РЕДКИЙ",
        Rarity.VeryRare => "ОЧЕНЬ РЕДКИЙ",
        Rarity.Legendary => "ЛЕГЕНДАРНЫЙ",
        _ => "",
    };

    public static string CategoryLabelRu(ItemCategory c) => c switch
    {
        ItemCategory.Equipment => "Экипировка",
        ItemCategory.Upgrade => "Улучшение",
        ItemCategory.KeyItem => "Сюжетный / Ключевой",
        ItemCategory.Consumable => "Бафф / Расходник",
        ItemCategory.Spell => "Заклинание",
        ItemCategory.Junk => "Мусор / Души",
        _ => "",
    };

    public static string KeepTagRu(KeepStatus k) => k switch
    {
        KeepStatus.DoNotDiscard => "НЕ ВЫБРАСЫВАТЬ",
        KeepStatus.UsefulInCombat => "ПОЛЕЗНО В БОЮ",
        KeepStatus.SellToFrampt => "МОЖНО ПРОДАТЬ ФРАМПТУ",
        KeepStatus.Situational => "ПО СИТУАЦИИ",
        _ => "",
    };

    public static string FormatDuration(TimeSpan t) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>12345 → "12 345" (неразрывный пробел; не зависит от культуры ОС).</summary>
    public static string FormatSouls(int souls) =>
        souls.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '\u00A0');
}
