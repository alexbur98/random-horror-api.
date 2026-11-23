using DS1Companion.Core.Models;

namespace DS1Companion.Core.Logic;

/// <summary>Стоимость следующего уровня для разных игр.</summary>
public static class LevelCurve
{
    /// <summary>Сколько стоит перейти с currentLevel на currentLevel+1; null — неизвестно (берите из памяти игры).</summary>
    public static int? CostForNextLevel(GameDefinition game, int currentLevel)
    {
        if (!game.HasLevels || currentLevel >= game.MaxLevel) return null;
        return game.LevelCurve switch
        {
            "ds1" => SoulsCalculator.CostForNextLevel(currentLevel),
            "souls3" => Souls3Cost(currentLevel),
            _ => null,
        };
    }

    /// <summary>
    /// Формула сообщества для Dark Souls 3 и Elden Ring:
    /// x = max(0, (L + 81 − 92) × 0.02); стоимость = floor((x + 0.1) × (L + 81)²) + 1, где L — текущий уровень.
    /// </summary>
    public static int Souls3Cost(int currentLevel)
    {
        if (currentLevel < 1) return 0;
        double l = currentLevel + 81;
        double x = Math.Max(0, (l - 92) * 0.02);
        return (int)Math.Floor((x + 0.1) * l * l) + 1;
    }

    public static int AffordableLevels(GameDefinition game, int souls, int currentLevel)
    {
        int levels = 0;
        long remaining = souls;
        while (levels < 100 && CostForNextLevel(game, currentLevel + levels) is { } cost && cost > 0 && remaining >= cost)
        {
            remaining -= cost;
            levels++;
        }
        return levels;
    }
}
