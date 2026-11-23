using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DS1Companion.Core.Models;

namespace DS1Companion.UI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null && !(value is string s && s.Length == 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"#RRGGBB" → SolidColorBrush (кэшируется и замораживается). Параметр — прозрачность 0..1.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value as string ?? "#9CA3AF";
        var opacity = parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var o) ? o : 1.0;
        return Cache.GetOrAdd($"{hex}|{opacity}", _ =>
        {
            var brush = new SolidColorBrush(HexToColorConverter.Parse(hex)) { Opacity = opacity };
            brush.Freeze();
            return brush;
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>"#RRGGBB" → Color. Параметр — альфа 0..1.</summary>
public sealed class HexToColorConverter : IValueConverter
{
    public static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch (FormatException) { return Colors.Gray; }
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var c = Parse(value as string ?? "#9CA3AF");
        if (parameter is string p && double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
            c.A = (byte)Math.Clamp(a * 255, 0, 255);
        return c;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Последние ~5% обратного отсчёта карточки — плавное затухание.</summary>
public sealed class FadeOutConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double f ? Math.Clamp(f / 0.05, 0, 1) : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class RarityLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Rarity r ? DisplayText.RarityLabelRu(r) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Подсказка про чтение памяти видна только при ожидании игры и если она не пустая.</summary>
public sealed class WaitingHintVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is ConnectionState.Waiting && values[1] is string { Length: > 0 }
            ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
