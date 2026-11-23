using DS1Companion.Core.Models;

namespace DS1Companion.Core.Game;

/// <summary>Заглушка, когда offsets.json отсутствует или повреждён: игра всегда «не найдена».</summary>
public sealed class DisconnectedReader : IGameStateReader
{
    public string Name => "Нет offsets.json";
    public GameSnapshot Read() => GameSnapshot.Disconnected;
    public void Dispose() { }
}
