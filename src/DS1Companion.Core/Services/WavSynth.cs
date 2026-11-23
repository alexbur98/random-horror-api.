using DS1Companion.Core.Models;

namespace DS1Companion.Core.Services;

/// <summary>
/// Генерация мягких звуков в WAV (PCM 16-bit, моно) прямо в памяти — без внешних файлов.
/// Громкость «запекается» в амплитуду, поэтому плеер может быть самым простым.
/// </summary>
public static class WavSynth
{
    public const int SampleRate = 44100;

    public static byte[] Render(SoundCue cue, double volume) => cue switch
    {
        // Лёгкий звон: две высокие ноты с мягким колокольным затуханием.
        SoundCue.VeryRareChime => Build(1.1, volume * 0.55, t =>
            Bell(t, 1318.5, 0.0, 1.6) * 0.6 + Bell(t, 1975.5, 0.12, 1.8) * 0.4),
        // Глубокий атмосферный аккорд: A2–E3–A3–C#4 с медленной атакой и хвостом.
        SoundCue.LegendaryChord => Build(3.2, volume * 0.6, t =>
            Pad(t, 110.0) * 0.32 + Pad(t, 164.81) * 0.26 + Pad(t, 220.0) * 0.22
            + Pad(t, 277.18) * 0.12 + Bell(t, 880.0, 0.35, 1.2) * 0.12),
        // Мягкое предупреждение: два низких «тук» без резкой атаки.
        SoundCue.DurabilityWarning => Build(0.9, volume * 0.5, t =>
            Soft(t, 392.0, 0.0) * 0.6 + Soft(t, 329.63, 0.28) * 0.6),
        _ => Build(0.1, 0, _ => 0),
    };

    private static double Bell(double t, double freq, double start, double decay)
    {
        var local = t - start;
        if (local < 0) return 0;
        var attack = Math.Min(1, local / 0.008);
        return attack * Math.Exp(-local * 3.0 / decay) *
               (Math.Sin(2 * Math.PI * freq * local) + 0.25 * Math.Sin(2 * Math.PI * freq * 2.01 * local));
    }

    private static double Pad(double t, double freq)
    {
        var attack = Math.Min(1, t / 0.6);
        var release = Math.Exp(-Math.Max(0, t - 1.2) * 1.6);
        var vibrato = 1 + 0.002 * Math.Sin(2 * Math.PI * 4.5 * t);
        return attack * release * (Math.Sin(2 * Math.PI * freq * vibrato * t) + 0.3 * Math.Sin(2 * Math.PI * freq * 2 * t));
    }

    private static double Soft(double t, double freq, double start)
    {
        var local = t - start;
        if (local < 0 || local > 0.5) return 0;
        var env = Math.Sin(Math.PI * Math.Min(1, local / 0.5));
        return env * env * Math.Sin(2 * Math.PI * freq * local);
    }

    private static byte[] Build(double seconds, double volume, Func<double, double> signal)
    {
        volume = Math.Clamp(volume, 0, 1);
        int samples = (int)(seconds * SampleRate);
        int dataBytes = samples * 2;
        var buffer = new byte[44 + dataBytes];
        using var ms = new MemoryStream(buffer);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataBytes); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(SampleRate); w.Write(SampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataBytes);

        int fadeOut = Math.Min(samples, SampleRate / 20);
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;
            double v = signal(t) * volume;
            if (i > samples - fadeOut) v *= (double)(samples - i) / fadeOut;
            v = Math.Tanh(v * 1.2) / Math.Tanh(1.2); // мягкий лимитер, без клиппинга
            w.Write((short)Math.Clamp(v * short.MaxValue * 0.9, short.MinValue, short.MaxValue));
        }
        return buffer;
    }
}
