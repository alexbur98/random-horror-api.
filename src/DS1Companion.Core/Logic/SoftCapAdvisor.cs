using DS1Companion.Core.Models;

namespace DS1Companion.Core.Logic;

public enum StatAdvice { Good, Efficient, Capped, Warning }

public sealed record StatHint(string Key, string NameRu, int Value, StatAdvice Advice, string HintRu);

/// <summary>Подсказки по порогам эффективности (soft caps). Пороги берутся из game.json.</summary>
public static class SoftCapAdvisor
{
    public static IReadOnlyList<StatHint> Analyze(GameDefinition game, CharacterStats stats) =>
        game.Stats.Where(d => stats.Values.ContainsKey(d.Key))
                  .Select(d => Analyze(d, stats.Values[d.Key]))
                  .ToList();

    public static StatHint Analyze(StatDefinition d, int value) => d.Kind switch
    {
        StatKind.SpellSlots => Slots(d, value),
        StatKind.Avoid => value > d.AvoidAbove
            ? new StatHint(d.Key, d.NameRu, value, StatAdvice.Warning, d.AvoidHintRu)
            : new StatHint(d.Key, d.NameRu, value, StatAdvice.Good, d.HintRu),
        _ => Cap(d, value),
    };

    public static int SlotsAt(StatDefinition d, int value) => d.SlotThresholds.Count(t => value >= t);

    private static StatHint Cap(StatDefinition d, int value)
    {
        if (d.Cap <= 0) return new StatHint(d.Key, d.NameRu, value, StatAdvice.Good, d.HintRu);
        var advice = value >= d.Cap + 10 ? StatAdvice.Capped : value >= d.Cap ? StatAdvice.Efficient : StatAdvice.Good;
        var suffix = advice switch
        {
            StatAdvice.Capped => $" Сейчас {value} — дальнейшая прокачка почти ничего не даёт.",
            StatAdvice.Efficient => $" Порог {d.Cap} достигнут.",
            _ => $" До порога: {d.Cap - value}.",
        };
        return new StatHint(d.Key, d.NameRu, value, advice, (d.HintRu + suffix).Trim());
    }

    private static StatHint Slots(StatDefinition d, int value)
    {
        var slots = SlotsAt(d, value);
        var next = d.SlotThresholds.FirstOrDefault(t => t > value);
        var hint = next == 0
            ? $"Ячеек заклинаний: {slots} (максимум)."
            : $"Ячеек заклинаний: {slots}. Следующая — на {next}. {d.HintRu}".Trim();
        return new StatHint(d.Key, d.NameRu, value, next == 0 ? StatAdvice.Capped : StatAdvice.Good, hint);
    }
}
