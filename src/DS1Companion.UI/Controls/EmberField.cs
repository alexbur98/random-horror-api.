using System.Windows;
using System.Windows.Media;
using DS1Companion.Core.Models;

namespace DS1Companion.UI.Controls;

/// <summary>
/// Лёгкая анимация частиц на фоне: искры, пепел, огоньки душ, лепестки или искры благодати — по теме игры.
/// Рисуется через OnRender (без сотен элементов в дереве), ограничена ~30 FPS и останавливается, когда окно скрыто.
/// </summary>
public sealed class EmberField : FrameworkElement
{
    private sealed class Particle
    {
        public double X, Y, Vx, Vy, Life, MaxLife, Size, Phase, Angle, Spin;
        public int Brush;
    }

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(EmberField), new PropertyMetadata(36, (d, _) => ((EmberField)d).Reset()));

    public static readonly DependencyProperty IsCalmProperty =
        DependencyProperty.Register(nameof(IsCalm), typeof(bool), typeof(EmberField), new PropertyMetadata(false));

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(GameTheme), typeof(EmberField), new PropertyMetadata(null, (d, _) => ((EmberField)d).ApplyTheme()));

    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    /// <summary>Приглушить частицы (например, пока открыта карточка).</summary>
    public bool IsCalm { get => (bool)GetValue(IsCalmProperty); set => SetValue(IsCalmProperty, value); }
    /// <summary>Тема игры: стиль и цвета частиц.</summary>
    public GameTheme? Theme { get => (GameTheme?)GetValue(ThemeProperty); set => SetValue(ThemeProperty, value); }

    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private Brush[] _brushes = CreateBrushes(new GameTheme().ParticleColors, ParticleStyle.Embers);
    private ParticleStyle _style = ParticleStyle.Embers;
    private TimeSpan _lastRender = TimeSpan.Zero;
    private bool _subscribed;

    public EmberField()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => UpdateSubscription();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => UpdateSubscription();
    }

    private void ApplyTheme()
    {
        var t = Theme ?? new GameTheme();
        _style = t.Particles;
        _brushes = CreateBrushes(t.ParticleColors.Count > 0 ? t.ParticleColors : new GameTheme().ParticleColors, _style);
        if (t.ParticleCount > 0) Count = t.ParticleCount;
        Reset();
    }

    private void Reset()
    {
        _particles.Clear();
        InvalidateVisual();
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

    private bool Falls => _style is ParticleStyle.Ash or ParticleStyle.Petals;

    private void Step(double dt)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        while (_particles.Count < Count) _particles.Add(Spawn(w, h, initial: true));
        double sway = _style switch { ParticleStyle.Petals => 22, ParticleStyle.Souls => 12, _ => 6 };
        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.Life += dt;
            p.Phase += dt;
            p.Angle += p.Spin * dt;
            p.X += (p.Vx + Math.Sin(p.Phase * 1.3) * sway) * dt;
            p.Y += p.Vy * dt;
            bool gone = p.Life >= p.MaxLife || (Falls ? p.Y > h + 12 : p.Y < -12);
            if (gone) _particles[i] = Spawn(w, h, initial: false);
        }
    }

    private Particle Spawn(double w, double h, bool initial)
    {
        double R(double a, double b) => a + _random.NextDouble() * (b - a);
        var (vy, size, life) = _style switch
        {
            ParticleStyle.Ash => (R(8, 20), R(0.8, 2.2), R(10, 18)),
            ParticleStyle.Souls => (-R(5, 12), R(1.6, 3.2), R(10, 18)),
            ParticleStyle.Petals => (R(16, 34), R(2.2, 3.8), R(12, 20)),
            ParticleStyle.Grace => (-R(6, 14), R(0.7, 1.6), R(8, 14)),
            _ => (-R(10, 32), R(0.8, 2.6), R(7, 16)),
        };
        var startY = Falls ? (initial ? R(0, h) : -R(4, 20)) : (initial ? R(0, h) : h + R(0, 20));
        return new Particle
        {
            X = R(0, w), Y = startY, Vx = R(-4, 4), Vy = vy, Size = size, MaxLife = life,
            Life = initial ? R(0, life) : 0, Phase = R(0, 10), Angle = R(0, 360), Spin = R(-60, 60),
            Brush = _random.Next(_brushes.Length),
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        var calm = IsCalm ? 0.35 : 1.0;
        foreach (var p in _particles)
        {
            var t = p.Life / p.MaxLife;
            var alpha = Math.Sin(Math.PI * t) * calm; // плавно появляется и гаснет
            if (_style is ParticleStyle.Embers or ParticleStyle.Grace) alpha *= 0.75 + 0.25 * Math.Sin(p.Phase * 7);
            dc.PushOpacity(Math.Clamp(alpha, 0, 1));
            var brush = _brushes[p.Brush % _brushes.Length];
            if (_style == ParticleStyle.Petals)
            {
                dc.PushTransform(new RotateTransform(p.Angle, p.X, p.Y));
                dc.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Size * 2.2, p.Size * 1.1);
                dc.Pop();
            }
            else
            {
                var radius = p.Size * (_style == ParticleStyle.Souls ? 4 : _style == ParticleStyle.Ash ? 1.6 : 3);
                dc.DrawEllipse(brush, null, new Point(p.X, p.Y), radius, radius);
            }
            dc.Pop();
        }
    }

    private static Brush[] CreateBrushes(IEnumerable<string> hexColors, ParticleStyle style) => hexColors.Select(hex =>
    {
        var c = ThemeManagerColor(hex);
        Brush b = style is ParticleStyle.Petals or ParticleStyle.Ash
            ? new SolidColorBrush(Color.FromArgb(style == ParticleStyle.Ash ? (byte)0x90 : (byte)0xC0, c.R, c.G, c.B))
            : new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0xE0, c.R, c.G, c.B), 0),
                new(Color.FromArgb(0x50, c.R, c.G, c.B), 0.35),
                new(Color.FromArgb(0x00, c.R, c.G, c.B), 1),
            });
        b.Freeze();
        return b;
    }).ToArray();

    private static Color ThemeManagerColor(string hex) => Services.ThemeManager.Parse(hex);
}
