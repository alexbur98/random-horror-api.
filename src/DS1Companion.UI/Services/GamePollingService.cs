using System.Diagnostics;
using System.Windows.Threading;
using DS1Companion.Core.Presentation;

namespace DS1Companion.UI.Services;

/// <summary>
/// Фоновый опрос ридера (память игры / демо) и UI-таймер для обратного отсчёта карточек.
/// Чтение памяти идёт вне UI-потока, обработка снимка — в UI-потоке.
/// </summary>
public sealed class GamePollingService : IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _uiTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTick;
    private Task? _loop;

    public GamePollingService(MainViewModel vm, Dispatcher dispatcher, int intervalMs)
    {
        _vm = vm;
        _dispatcher = dispatcher;
        _interval = TimeSpan.FromMilliseconds(Math.Clamp(intervalMs, 100, 2000));
        _uiTimer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        _uiTimer.Tick += (_, _) =>
        {
            var now = _clock.Elapsed;
            _vm.Tick(now - _lastTick);
            _lastTick = now;
        };
    }

    public void Start()
    {
        _lastTick = _clock.Elapsed;
        _uiTimer.Start();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var reader = _vm.CurrentReader;
                var snapshot = reader.Read();
                await _dispatcher.InvokeAsync(() => _vm.OnSnapshot(reader, snapshot), DispatcherPriority.Background, ct);
                await Task.Delay(_interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            }
        }
    }

    public void Dispose()
    {
        _uiTimer.Stop();
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { /* остановлено */ }
        _cts.Dispose();
    }
}
