using DS1Companion.Core.Models;

namespace DS1Companion.Core.Logic;

public enum StatAdvice { Good, Efficient, Capped, Warning }

public sealed record StatHint(string Key, string NameRu, int Value, StatAdvice Advice, string HintRu);

/// <summary>Подсказки по порогам эффективности (soft caps) Dark Souls.</summary>
public static class SoftCapAdvisor
{
    private static readonly int[] AttunementSlots = { 10, 12, 14, 16, 19, 23, 28, 34, 41, 50 };

    public static int AttunementSlotsAt(int attunement) => AttunementSlots.Count(t => attunement >= t);

    public static IReadOnlyList<StatHint> Analyze(CharacterStats s) => new[]
    {
        CapStat("vitality", "Живучесть", s.Vitality, 40, "Здоровье. Эффективно до 40, дальше прирост слабый."),
        Attunement(s.Attunement),
        CapStat("endurance", "Стойкость", s.Endurance, 40, "Выносливость и вес. Выносливость перестаёт расти после 40."),
        CapStat("strength", "Сила", s.Strength, 40, "Урон тяжёлого оружия. Выше 40 почти не растёт."),
        CapStat("dexterity", "Ловкость", s.Dexterity, 40, "Урон быстрого оружия. Выше 40 почти не растёт."),
        new StatHint("resistance", "Сопротивление", s.Resistance,
            s.Resistance > 12 ? StatAdvice.Warning : StatAdvice.Good,
            s.Resistance > 12
                ? "Не качайте! Сопротивление почти бесполезно — очки лучше вложить в Живучесть."
                : "Не качайте: защиту лучше дают броня и кольца."),
        CapStat("intelligence", "Интеллект", s.Intelligence, 40, "Сила колдовства. Порог 40, у некоторых катализаторов — 50."),
        CapStat("faith", "Вера", s.Faith, 40, "Сила чудес. Порог 40, у некоторых талисманов — 50."),
    };

    private static StatHint CapStat(string key, string name, int value, int cap, string hint)
    {
        var advice = value >= cap + 10 ? StatAdvice.Capped : value >= cap ? StatAdvice.Efficient : StatAdvice.Good;
        var suffix = advice switch
        {
            StatAdvice.Capped => $" Сейчас {value} — дальнейшая прокачка почти ничего не даёт.",
            StatAdvice.Efficient => $" Порог {cap} достигнут.",
            _ => $" До порога: {cap - value}.",
        };
        return new StatHint(key, name, value, advice, hint + suffix);
    }

    private static StatHint Attunement(int value)
    {
        var slots = AttunementSlotsAt(value);
        var next = AttunementSlots.FirstOrDefault(t => t > value);
        var hint = next == 0
            ? $"Слотов заклинаний: {slots} (максимум)."
            : $"Слотов заклинаний: {slots}. Следующий слот — на {next}. Без магии качать не нужно.";
        return new StatHint("attunement", "Ученость", value, next == 0 ? StatAdvice.Capped : StatAdvice.Good, hint);
    }
}
