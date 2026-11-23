using System.Collections.Concurrent;
using System.IO;
using System.Media;
using DS1Companion.Core.Models;
using DS1Companion.Core.Services;

namespace DS1Companion.UI.Services;

/// <summary>Проигрывает сгенерированные WAV (см. <see cref="WavSynth"/>) в фоне, громкость «запечена» в сэмплы.</summary>
public sealed class WpfSoundService : ISoundService
{
    private readonly ConcurrentDictionary<(SoundCue, int), byte[]> _cache = new();
    private double _volume = 0.6;

    public double Volume { get => _volume; set => _volume = Math.Clamp(value, 0, 1); }
    public bool IsMuted { get; set; }

    public void Play(SoundCue cue)
    {
        if (IsMuted || Volume <= 0.001) return;
        var step = (int)Math.Round(Volume * 20); // шаг громкости 5% — кэш не разрастается
        var wav = _cache.GetOrAdd((cue, step), k => WavSynth.Render(k.Item1, k.Item2 / 20.0));
        Task.Run(() =>
        {
            try
            {
                using var stream = new MemoryStream(wav, writable: false);
                using var player = new SoundPlayer(stream);
                player.PlaySync();
            }
            catch (Exception)
            {
                // Нет аудиоустройства — молча продолжаем.
            }
        });
    }
}
