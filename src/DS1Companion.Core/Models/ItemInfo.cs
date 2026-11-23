using System.Text.Json.Serialization;

namespace DS1Companion.Core.Models;

/// <summary>Запись базы знаний из Data/{игра}/items.json.</summary>
public sealed record ItemInfo
{
    public required string Id { get; init; }
    public required string NameRu { get; init; }
    public required string NameEn { get; init; }
    public ItemCategory Category { get; init; }
    public Rarity Rarity { get; init; }
    public string RarityReason { get; init; } = "";
    public string SummaryRu { get; init; } = "";
    public KeepStatus KeepStatus { get; init; }
    public string? IconKey { get; init; }
    public string? IconUrl { get; init; }

    /// <summary>Тип предмета в игре (для сопоставления с памятью). Может отсутствовать.</summary>
    public ItemKind? GameKind { get; init; }

    /// <summary>ID из EquipParam* игры. Для оружия — базовый ID без заточки/инфузии.</summary>
    public int? GameId { get; init; }

    /// <summary>Явно заданная векторная иконка; иначе выводится из категории/типа.</summary>
    public IconGlyph? Glyph { get; init; }

    /// <summary>Подпапка иконок игры (ds1, ds2…): Assets/Icons/{IconFolder}/{id}.png. Заполняется при загрузке.</summary>
    [JsonIgnore]
    public string? IconFolder { get; set; }

    [JsonIgnore]
    public IconGlyph EffectiveGlyph => Glyph ?? (Category, GameKind) switch
    {
        (ItemCategory.Equipment, ItemKind.Ring) => IconGlyph.Ring,
        (ItemCategory.Equipment, ItemKind.Armor) => IconGlyph.Armor,
        (ItemCategory.Equipment, _) => IconGlyph.Sword,
        (ItemCategory.Upgrade, _) => IconGlyph.Titanite,
        (ItemCategory.KeyItem, _) => IconGlyph.Key,
        (ItemCategory.Consumable, _) => IconGlyph.Flask,
        (ItemCategory.Spell, _) => IconGlyph.Scroll,
        (ItemCategory.Junk, _) => IconGlyph.Soul,
        _ => IconGlyph.Unknown,
    };

    public static ItemInfo Unknown(ItemKind kind, int gameId) => new()
    {
        Id = $"unknown_{(int)kind}_{gameId}",
        NameRu = $"Неизвестный предмет #{gameId}",
        NameEn = $"Unknown item #{gameId}",
        Category = kind == ItemKind.Goods ? ItemCategory.Consumable : ItemCategory.Equipment,
        Rarity = Rarity.Common,
        RarityReason = "Нет в базе знаний",
        SummaryRu = "Этого предмета пока нет в базе знаний (Data/{игра}/items.json). Его можно добавить вручную — см. README.",
        KeepStatus = KeepStatus.Situational,
        GameKind = kind,
        GameId = gameId,
        Glyph = IconGlyph.Unknown,
    };
}
