using DS1Companion.Core.Models;

namespace DS1Companion.Core.Data;

/// <summary>Все игры из Data/*/game.json. Каждая игра — отдельная база знаний.</summary>
public sealed class GameCatalog
{
    private readonly Dictionary<GameKey, KnowledgeBase> _games;

    public IReadOnlyList<KnowledgeBase> Games { get; }

    public GameCatalog(IEnumerable<KnowledgeBase> games)
    {
        Games = games.OrderBy(g => g.Game.Key).ToList();
        _games = new Dictionary<GameKey, KnowledgeBase>();
        foreach (var g in Games)
            if (!_games.TryAdd(g.Game.Key, g))
                throw new InvalidDataException($"Игра {g.Game.Key} описана дважды");
        if (Games.Count == 0) throw new InvalidDataException("Не найдено ни одной игры в Data/*/game.json");
    }

    public static GameCatalog LoadFromDirectory(string dataDir) =>
        new(Directory.EnumerateDirectories(dataDir)
                     .Where(d => File.Exists(Path.Combine(d, "game.json")))
                     .Select(KnowledgeBase.LoadFromDirectory));

    public KnowledgeBase? Find(GameKey key) => _games.GetValueOrDefault(key);

    public KnowledgeBase Get(GameKey key) =>
        Find(key) ?? throw new KeyNotFoundException($"Нет данных для игры {key}");

    public KnowledgeBase Default => Games[0];
}
