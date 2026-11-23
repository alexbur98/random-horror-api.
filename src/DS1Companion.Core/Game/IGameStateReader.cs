using DS1Companion.Core.Models;

namespace DS1Companion.Core.Game;

/// <summary>Источник снимков состояния игры. Реализации обязаны быть read-only.</summary>
public interface IGameStateReader : IDisposable
{
    string Name { get; }
    /// <summary>Возвращает текущий снимок. Не бросает исключений: при проблемах — <see cref="GameSnapshot.Disconnected"/>.</summary>
    GameSnapshot Read();
}
