using System.Text.Json.Serialization;

namespace DS1Companion.Core.Models;

/// <summary>Поддерживаемые игры серии.</summary>
public enum GameKey
{
    DarkSouls1,
    DarkSouls2,
    DarkSouls3,
    Sekiro,
    EldenRing,
}

public enum StatKind
{
    /// <summary>Обычная характеристика с порогом эффективности.</summary>
    Cap,
    /// <summary>Ячейки заклинаний по порогам (Ученость, Настройка).</summary>
    SpellSlots,
    /// <summary>Не стоит качать выше значения AvoidAbove.</summary>
    Avoid,
}

public sealed record StatDefinition
{
    public required string Key { get; init; }
    public required string NameRu { get; init; }
    public StatKind Kind { get; init; } = StatKind.Cap;
    public int Cap { get; init; }
    public string HintRu { get; init; } = "";
    public List<int> SlotThresholds { get; init; } = new();
    public int AvoidAbove { get; init; }
    public string AvoidHintRu { get; init; } = "";
}

/// <summary>Анимация фона.</summary>
public enum ParticleStyle
{
    /// <summary>Тлеющие искры поднимаются вверх (DS1).</summary>
    Embers,
    /// <summary>Хлопья пепла медленно падают (DS3).</summary>
    Ash,
    /// <summary>Мягкие огоньки душ плывут вверх (DS2).</summary>
    Souls,
    /// <summary>Лепестки падают и кружатся (Sekiro).</summary>
    Petals,
    /// <summary>Золотые искры благодати (Elden Ring).</summary>
    Grace,
}

/// <summary>Оформление игры: палитра, шрифт заголовков и анимация фона. Цвета — "#RRGGBB" или "#AARRGGBB".</summary>
public sealed record GameTheme
{
    public string NameRu { get; init; } = "Пламя";
    public string Accent { get; init; } = "#C8A96A";
    public string AccentDim { get; init; } = "#7D6A45";
    public string AccentBright { get; init; } = "#F1DDAA";
    public string Background { get; init; } = "#08090C";
    public string PanelTop { get; init; } = "#12151B";
    public string PanelBottom { get; init; } = "#0C0E12";
    public string Line { get; init; } = "#232733";
    public string Text { get; init; } = "#E7E2D6";
    public string TextMuted { get; init; } = "#8D8A82";
    public string Glow { get; init; } = "#FF7A2E";
    public string TitleBar { get; init; } = "#CC07080A";
    public string Track { get; init; } = "#1E222B";
    public string Sheet { get; init; } = "#F20E1015";
    public string TitleFont { get; init; } = "Cinzel, Georgia, Times New Roman";
    public ParticleStyle Particles { get; init; } = ParticleStyle.Embers;
    public List<string> ParticleColors { get; init; } = new() { "#FF8A3D", "#FFB05C", "#E06A2A" };
    public int ParticleCount { get; init; } = 34;

    public IEnumerable<string> AllColors() =>
        new[] { Accent, AccentDim, AccentBright, Background, PanelTop, PanelBottom, Line, Text, TextMuted, Glow, TitleBar, Track, Sheet }
            .Concat(ParticleColors);
}

/// <summary>Стартовое состояние демо-режима для игры.</summary>
public sealed record DemoProfile
{
    public string CharacterName { get; init; } = "Безымянный";
    public int Deaths { get; init; }
    public int Souls { get; init; }
    public int Level { get; init; } = 1;
    public string? LocationId { get; init; }
    public string? WeaponItemId { get; init; }
    public int WeaponLevel { get; init; }
    public int WeaponMaxDurability { get; init; } = 100;
    public string? HealingItemId { get; init; }
    public int HealingCount { get; init; } = 5;
    public string? LegendaryItemId { get; init; }
    public string? KeyItemId { get; init; }
    public Dictionary<string, int> Stats { get; init; } = new();
}

/// <summary>
/// Описание игры из Data/{папка}/game.json: термины, механики и правила прокачки.
/// Всё, что отличает DS1 от DS2, Sekiro или Elden Ring, должно жить здесь, а не в коде.
/// </summary>
public sealed record GameDefinition
{
    public GameKey Key { get; init; }
    public required string TitleRu { get; init; }
    public required string ShortTitle { get; init; }
    /// <summary>Имена процессов без .exe.</summary>
    public List<string> ProcessNames { get; init; } = new();

    /// <summary>«Души», «Руны», «Сэн».</summary>
    public string CurrencyRu { get; init; } = "Души";
    /// <summary>Родительный падеж множественного числа: «душ», «рун», «сэн».</summary>
    public string CurrencyGenitiveRu { get; init; } = "душ";

    public bool HasLevels { get; init; } = true;
    /// <summary>ds1 | souls3 (DS3 и Elden Ring) | memory (берётся из памяти игры) | none.</summary>
    public string LevelCurve { get; init; } = "none";
    public int MaxLevel { get; init; } = 713;

    public bool HasBloodstain { get; init; } = true;
    public string BloodstainTitleRu { get; init; } = "Кровавое пятно";
    /// <summary>{0} — сумма, {1} — валюта в родительном падеже.</summary>
    public string BloodstainTextRu { get; init; } = "{0} {1} ждут в кровавом пятне. Вернитесь к месту гибели и не умирайте по пути.";

    public bool HasHollowing { get; init; }
    public string HollowingTitleRu { get; init; } = "";
    /// <summary>Подпись кнопки демо, которая снимает эффект («Чучело человека», «Капля драконьей крови»).</summary>
    public string HollowingCureRu { get; init; } = "Восстановить";
    /// <summary>{0} — число смертей без восстановления.</summary>
    public string HollowingTextRu { get; init; } = "";

    public bool HasWeaponUpgrades { get; init; } = true;
    public bool HasDurability { get; init; } = true;
    public string RepairHintRu { get; init; } = "почините у кузнеца";

    /// <summary>Как подписать тег SellToFrampt в этой игре.</summary>
    public string SellTagRu { get; init; } = "МОЖНО ПРОДАТЬ";
    public string DefaultTipRu { get; init; } = "";
    public string StatsSheetHintRu { get; init; } = "Пороги эффективности (soft caps): после них каждое очко даёт заметно меньше.";

    public List<StatDefinition> Stats { get; init; } = new();
    public GameTheme Theme { get; init; } = new();
    /// <summary>Пояснение про чтение памяти (например, античит), показывается в README и при ожидании игры.</summary>
    public string MemoryNoteRu { get; init; } = "";
    public DemoProfile Demo { get; init; } = new();

    /// <summary>Каталог данных игры; заполняется при загрузке.</summary>
    [JsonIgnore] public string Folder { get; set; } = "";
}
