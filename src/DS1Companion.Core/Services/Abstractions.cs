using DS1Companion.Core.Models;

namespace DS1Companion.Core.Services;

public interface IClock
{
    DateTime Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}

public interface ISoundService
{
    double Volume { get; set; }
    bool IsMuted { get; set; }
    void Play(SoundCue cue);
}

public sealed class NullSoundService : ISoundService
{
    public double Volume { get; set; } = 0.6;
    public bool IsMuted { get; set; }
    public List<SoundCue> Played { get; } = new();
    public void Play(SoundCue cue) => Played.Add(cue);
}
