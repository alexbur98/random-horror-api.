using DS1Companion.Core.Models;

namespace DS1Companion.Core.Logic;

public readonly record struct InventoryGain(ItemKind Kind, int GameId, int Quantity);

/// <summary>Сравнение двух снимков инвентаря: что появилось нового или прибавилось в количестве.</summary>
public static class InventoryDiff
{
    public static IReadOnlyList<InventoryGain> Compute(IReadOnlyList<InventoryEntry>? previous, IReadOnlyList<InventoryEntry>? current)
    {
        if (previous is null || current is null) return Array.Empty<InventoryGain>();

        var before = Aggregate(previous);
        var gains = new List<InventoryGain>();
        foreach (var (key, qty) in Aggregate(current))
        {
            before.TryGetValue(key, out var old);
            if (qty > old)
                gains.Add(new InventoryGain(key.Kind, key.GameId, qty - old));
        }
        return gains;
    }

    private static Dictionary<(ItemKind Kind, int GameId), int> Aggregate(IEnumerable<InventoryEntry> entries)
    {
        var map = new Dictionary<(ItemKind, int), int>();
        foreach (var e in entries)
        {
            if (e.GameId < 0) continue; // пустые слоты в памяти игры
            var key = (e.Kind, e.GameId);
            map[key] = map.GetValueOrDefault(key) + Math.Max(1, e.Quantity);
        }
        return map;
    }
}
