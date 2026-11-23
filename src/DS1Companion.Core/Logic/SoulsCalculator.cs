namespace DS1Companion.Core.Logic;

/// <summary>
/// Стоимость уровня в Dark Souls. Для уровней 2–11 — табличные значения,
/// начиная с 12 — формула сообщества: round(0.02x³ + 3.06x² + 105.6x − 895), где x — целевой уровень.
/// </summary>
public static class SoulsCalculator
{
    public const int MaxLevel = 713;

    private static readonly int[] EarlyLevelCosts =
    {
        // индекс = целевой уровень
        0, 0, 673, 690, 707, 724, 741, 758, 775, 793, 811, 829,
    };

    /// <summary>Сколько душ стоит перейти с уровня (targetLevel-1) на targetLevel.</summary>
    public static int CostToReach(int targetLevel)
    {
        if (targetLevel <= 1 || targetLevel > MaxLevel) return 0;
        if (targetLevel < EarlyLevelCosts.Length) return EarlyLevelCosts[targetLevel];
        double x = targetLevel;
        return (int)Math.Round(0.02 * x * x * x + 3.06 * x * x + 105.6 * x - 895, MidpointRounding.AwayFromZero);
    }

    /// <summary>Сколько стоит следующий уровень с текущего.</summary>
    public static int CostForNextLevel(int currentLevel) => CostToReach(currentLevel + 1);

    /// <summary>Доля прогресса 0..1 «души на руках / нужно на следующий уровень».</summary>
    public static double Progress(int souls, int currentLevel)
    {
        var cost = CostForNextLevel(currentLevel);
        if (cost <= 0) return 1;
        return Math.Clamp((double)Math.Max(0, souls) / cost, 0, 1);
    }

    /// <summary>Сколько уровней подряд можно купить на текущие души.</summary>
    public static int AffordableLevels(int souls, int currentLevel)
    {
        int levels = 0;
        long remaining = souls;
        while (currentLevel + levels < MaxLevel)
        {
            var cost = CostForNextLevel(currentLevel + levels);
            if (cost <= 0 || remaining < cost) break;
            remaining -= cost;
            levels++;
        }
        return levels;
    }
}
