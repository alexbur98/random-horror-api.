using DS1Companion.Core.Memory;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Game;

/// <summary>
/// Автоопределение игры: по очереди пытается подключиться к процессам всех поддерживаемых игр
/// (у каждой свой offsets.json) и читает ту, что запущена.
/// </summary>
public sealed class MultiGameReader : IGameStateReader
{
    private readonly IReadOnlyList<GameMemoryReader> _readers;
    private GameMemoryReader? _active;
    private int _next;

    public MultiGameReader(IEnumerable<GameMemoryReader> readers) => _readers = readers.ToList();

    public string Name => _active?.Name ?? "Авто";
    public GameDefinition? ActiveGame => _active?.Game;

    public GameSnapshot Read()
    {
        if (_active is not null)
        {
            var s = _active.Read();
            if (s.IsConnected) return s;
            _active = null;
        }
        if (_readers.Count == 0) return GameSnapshot.Disconnected;

        // За один опрос пробуем одну игру — чтобы не задерживать цикл поиском всех процессов сразу.
        var reader = _readers[_next];
        _next = (_next + 1) % _readers.Count;
        var snapshot = reader.Read();
        if (snapshot.IsConnected) _active = reader;
        return snapshot;
    }

    public void Dispose()
    {
        foreach (var r in _readers) r.Dispose();
    }
}
