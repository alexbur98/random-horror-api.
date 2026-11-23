namespace DS1Companion.Core.Models;

/// <summary>Пять уровней редкости. Порядок значений важен: фильтрация идёт по «не ниже».</summary>
public enum Rarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    VeryRare = 3,
    Legendary = 4,
}

/// <summary>Категория предмета для ленты на карточке.</summary>
public enum ItemCategory
{
    Equipment,
    Upgrade,
    KeyItem,
    Consumable,
    Spell,
    Junk,
}

/// <summary>Явный тег-подсказка: что делать с предметом.</summary>
public enum KeepStatus
{
    DoNotDiscard,
    UsefulInCombat,
    SellToFrampt,
    Situational,
}

/// <summary>Тип предмета в инвентаре DS1 (старшие биты категории в памяти игры).</summary>
public enum ItemKind
{
    Weapon = 0,
    Armor = 1,
    Ring = 2,
    Goods = 4,
}

/// <summary>Векторные fallback-иконки по категориям.</summary>
public enum IconGlyph
{
    Sword,
    Shield,
    Armor,
    Key,
    Titanite,
    Ember,
    Flask,
    Ring,
    Scroll,
    Soul,
    Unknown,
}

public enum ConnectionState
{
    Waiting,
    Connected,
    Demo,
}

public enum SoundCue
{
    VeryRareChime,
    LegendaryChord,
    DurabilityWarning,
}
