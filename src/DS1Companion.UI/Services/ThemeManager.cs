using System.Windows;
using System.Windows.Media;
using DS1Companion.Core.Models;

namespace DS1Companion.UI.Services;

/// <summary>
/// Перекрашивает приложение под игру: подменяет кисти и шрифт в ресурсах приложения.
/// Все элементы, которые должны меняться, ссылаются на эти ключи через DynamicResource.
/// </summary>
public static class ThemeManager
{
    /// <summary>Ключи ресурсов, которые меняются вместе с игрой.</summary>
    public static readonly string[] ThemedKeys =
    {
        "ObsidianBrush", "PanelBrush", "PanelRaisedBrush", "LineBrush", "GoldBrush", "GoldDimBrush", "TextBrush",
        "TextMutedBrush", "HoverBrush", "PanelGradientBrush", "SoulsFillBrush", "WarmGlowBrush", "TitleBarBrush",
        "TrackBrush", "SheetBrush", "SerifFont",
    };

    public static void Apply(ResourceDictionary resources, GameTheme t)
    {
        var accent = Parse(t.Accent);
        resources["ObsidianBrush"] = Solid(t.Background);
        resources["PanelBrush"] = Solid(t.PanelBottom);
        resources["PanelRaisedBrush"] = Solid(t.PanelTop);
        resources["LineBrush"] = Solid(t.Line);
        resources["GoldBrush"] = Solid(t.Accent);
        resources["GoldDimBrush"] = Solid(t.AccentDim);
        resources["TextBrush"] = Solid(t.Text);
        resources["TextMutedBrush"] = Solid(t.TextMuted);
        resources["HoverBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0x1A, accent.R, accent.G, accent.B)));
        resources["TitleBarBrush"] = Solid(t.TitleBar);
        resources["TrackBrush"] = Solid(t.Track);
        resources["SheetBrush"] = Solid(t.Sheet);
        resources["PanelGradientBrush"] = Freeze(new LinearGradientBrush(Parse(t.PanelTop), Parse(t.PanelBottom), 90));
        resources["SoulsFillBrush"] = Freeze(new LinearGradientBrush(new GradientStopCollection
        {
            new(Parse(t.AccentDim), 0), new(accent, 0.8), new(Parse(t.AccentBright), 1),
        }, 0));
        var glow = Parse(t.Glow);
        resources["WarmGlowBrush"] = Freeze(new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0x33, glow.R, glow.G, glow.B), 0), new(Color.FromArgb(0, glow.R, glow.G, glow.B), 1),
        })
        { Center = new Point(0.5, 1.05), GradientOrigin = new Point(0.5, 1.05), RadiusX = 0.8, RadiusY = 0.45 });
        resources["SerifFont"] = new FontFamily(string.IsNullOrWhiteSpace(t.TitleFont) ? "Georgia" : t.TitleFont);
    }

    public static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Colors.Gray; }
    }

    private static SolidColorBrush Solid(string hex) => Freeze(new SolidColorBrush(Parse(hex)));

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
