using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DS1Companion.Core.Services;

namespace DS1Companion.UI.Controls;

/// <summary>Иконка предмета: PNG из Assets/Icons или векторный глиф категории.</summary>
public sealed class ItemIcon : Grid
{
    private static readonly ConcurrentDictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(IconSource), typeof(ItemIcon), new PropertyMetadata(null, (d, _) => ((ItemIcon)d).Rebuild()));

    public static readonly DependencyProperty GlyphBrushProperty = DependencyProperty.Register(
        nameof(GlyphBrush), typeof(Brush), typeof(ItemIcon), new PropertyMetadata(Brushes.Gray, (d, _) => ((ItemIcon)d).Rebuild()));

    public IconSource? Source { get => (IconSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public Brush GlyphBrush { get => (Brush)GetValue(GlyphBrushProperty); set => SetValue(GlyphBrushProperty, value); }

    private void Rebuild()
    {
        Children.Clear();
        if (Source is not { } src) return;

        if (src.FilePath is { } path && Load(path) is { } bmp)
        {
            var image = new Image { Source = bmp, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            Children.Add(image);
            return;
        }

        var geometry = TryFindResource($"Glyph.{src.Glyph}") as Geometry ?? TryFindResource("Glyph.Unknown") as Geometry;
        Children.Add(new System.Windows.Shapes.Path { Data = geometry, Fill = GlyphBrush, Stretch = Stretch.Uniform, Margin = new Thickness(2) });
    }

    private static BitmapSource? Load(string path) => Cache.GetOrAdd(path, p =>
    {
        try
        {
            if (!File.Exists(p)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // не держим файл открытым
            bmp.UriSource = new Uri(p, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null; // повреждённый PNG — используем векторную иконку
        }
    });
}
