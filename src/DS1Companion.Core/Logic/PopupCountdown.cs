namespace DS1Companion.Core.Logic;

/// <summary>
/// Детерминированный таймер карточки предмета: время подаётся извне через <see cref="Advance"/>,
/// поэтому логику паузы при наведении можно покрыть тестами без реального ожидания.
/// </summary>
public sealed class PopupCountdown
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(10);

    public TimeSpan Duration { get; }
    public TimeSpan Elapsed { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsExpired { get; private set; }

    public TimeSpan Remaining => Duration - Elapsed < TimeSpan.Zero ? TimeSpan.Zero : Duration - Elapsed;
    /// <summary>1.0 — только что появилась, 0.0 — истекла.</summary>
    public double RemainingFraction => Duration <= TimeSpan.Zero ? 0 : Remaining.TotalMilliseconds / Duration.TotalMilliseconds;

    public event EventHandler? Expired;

    public PopupCountdown(TimeSpan? duration = null)
    {
        Duration = duration ?? DefaultDuration;
    }

    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    /// <summary>Возвращает true, если таймер истёк именно на этом шаге.</summary>
    public bool Advance(TimeSpan delta)
    {
        if (IsExpired || IsPaused || delta <= TimeSpan.Zero) return false;
        Elapsed += delta;
        if (Elapsed < Duration) return false;
        Elapsed = Duration;
        IsExpired = true;
        Expired?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Принудительно закрыть карточку (клик «закрыть»).</summary>
    public void Complete()
    {
        if (IsExpired) return;
        Elapsed = Duration;
        IsExpired = true;
        Expired?.Invoke(this, EventArgs.Empty);
    }
}
