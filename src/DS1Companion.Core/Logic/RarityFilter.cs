using DS1Companion.Core.Models;

namespace DS1Companion.Core.Logic;

public static class RarityFilter
{
    public static bool IsAtLeast(this Rarity rarity, Rarity minimum) => rarity >= minimum;

    public static IEnumerable<ItemInfo> AtLeast(IEnumerable<ItemInfo> items, Rarity minimum) =>
        items.Where(i => i.Rarity >= minimum);

    /// <summary>Нужно ли показывать большую карточку. Ключевые предметы показываем всегда.</summary>
    public static bool ShouldShowPopup(ItemInfo item, Rarity minimum) =>
        item.Rarity >= minimum || item.Category == ItemCategory.KeyItem;

    /// <summary>Попадает ли предмет в коллекцию редкого лута (Rare и выше).</summary>
    public static bool IsCollectible(ItemInfo item) => item.Rarity >= Rarity.Rare;

    /// <summary>Звук, который нужно проиграть при показе карточки (null — без звука).</summary>
    public static SoundCue? CueFor(Rarity rarity) => rarity switch
    {
        Rarity.Legendary => SoundCue.LegendaryChord,
        Rarity.VeryRare => SoundCue.VeryRareChime,
        _ => null,
    };
}
