using System.Windows;
using System.Windows.Media;

namespace DS1Companion.UI.Controls;

/// <summary>
/// Лёгкая анимация тлеющих искр на фоне. Рисуется через OnRender (без сотен элементов в дереве),
/// ограничена ~30 FPS и останавливается, когда окно свёрнуто или скрыто.
/// </summary>
public sealed class EmberField : FrameworkElement
{
    private sealed class Ember
    {
        public double X, Y, Vx, Vy, Life, MaxLife, Size, Phase;
    }

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(EmberField), new PropertyMetadata(36));

    public static readonly DependencyProperty IsCalmProperty =
        DependencyProperty.Register(nameof(IsCalm), typeof(bool), typeof(EmberField), new PropertyMetadata(false));

    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    /// <summary>Приглушить искры (например, пока открыта карточка).</summary>
    public bool IsCalm { get => (bool)GetValue(IsCalmProperty); set => SetValue(IsCalmProperty, value); }

    private static readonly Brush[] Brushes = CreateBrushes();
    private readonly List<Ember> _embers = new();
    private readonly Random _random = new();
    private TimeSpan _lastRender = TimeSpan.Zero;
    private bool _subscribed;

    public EmberField()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => UpdateSubscription();
    }

    private void UpdateSubscription()
    {
        if (IsVisible && !_subscribed)
        {
            CompositionTarget.Rendering += OnRendering;
            _subscribed = true;
        }
        else if (!IsVisible) Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        CompositionTarget.Rendering -= OnRendering;
        _subscribed = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        var dt = (args.RenderingTime - _lastRender).TotalSeconds;
        if (dt < 1.0 / 30) return;
        _lastRender = args.RenderingTime;
        Step(Math.Min(dt, 0.1));
        InvalidateVisual();
    }

    private void Step(double dt)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        while (_embers.Count < Count) _embers.Add(Spawn(w, h, initial: true));
        for (int i = 0; i < _embers.Count; i++)
        {
            var e = _embers[i];
            e.Life += dt;
            e.Phase += dt;
            e.X += (e.Vx + Math.Sin(e.Phase * 1.3) * 6) * dt;
            e.Y += e.Vy * dt;
            if (e.Life >= e.MaxLife || e.Y < -10) _embers[i] = Spawn(w, h, initial: false);
        }
    }

    private Ember Spawn(double w, double h, bool initial)
    {
        var maxLife = 7 + _random.NextDouble() * 9;
        return new Ember
        {
            X = _random.NextDouble() * w,
            Y = initial ? _random.NextDouble() * h : h + _random.NextDouble() * 20,
            Vx = (_random.NextDouble() - 0.5) * 8,
            Vy = -(10 + _random.NextDouble() * 22),
            MaxLife = maxLife,
            Life = initial ? _random.NextDouble() * maxLife : 0,
            Size = 0.8 + _random.NextDouble() * 1.8,
            Phase = _random.NextDouble() * 10,
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        var calm = IsCalm ? 0.35 : 1.0;
        foreach (var e in _embers)
        {
            var t = e.Life / e.MaxLife;
            var alpha = Math.Sin(Math.PI * t) * calm; // плавно загорается и гаснет
            var flicker = 0.75 + 0.25 * Math.Sin(e.Phase * 7);
            dc.PushOpacity(Math.Clamp(alpha * flicker, 0, 1));
            dc.DrawEllipse(Brushes[(int)(e.Size * 10) % Brushes.Length], null, new Point(e.X, e.Y), e.Size * 3, e.Size * 3);
            dc.Pop();
        }
    }

    private static Brush[] CreateBrushes()
    {
        var colors = new[] { Color.FromRgb(0xFF, 0x8A, 0x3D), Color.FromRgb(0xFF, 0xB0, 0x5C), Color.FromRgb(0xE0, 0x6A, 0x2A) };
        return colors.Select(c =>
        {
            var b = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0xE0, c.R, c.G, c.B), 0),
                new(Color.FromArgb(0x50, c.R, c.G, c.B), 0.35),
                new(Color.FromArgb(0x00, c.R, c.G, c.B), 1),
            });
            b.Freeze();
            return (Brush)b;
        }).ToArray();
    }
}
