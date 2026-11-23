using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DS1Companion.UI.Controls;

/// <summary>Процедурная зернистая текстура для фона (вместо картинки в ресурсах).</summary>
public static class NoiseTexture
{
    public static Brush Create(int size = 160, int seed = 7)
    {
        var random = new Random(seed);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            byte v = (byte)random.Next(90, 200);
            byte a = (byte)random.Next(0, 16);
            pixels[i * 4 + 0] = v;
            pixels[i * 4 + 1] = v;
            pixels[i * 4 + 2] = (byte)Math.Min(255, v + 6);
            pixels[i * 4 + 3] = a;
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bmp.Freeze();
        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, size, size),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }
}
