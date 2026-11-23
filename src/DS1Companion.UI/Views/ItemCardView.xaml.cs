using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DS1Companion.Core.Presentation;

namespace DS1Companion.UI.Views;

public partial class ItemCardView : UserControl
{
    public ItemCardView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnCardChanged();
    }

    private ItemCardViewModel? Card => DataContext as ItemCardViewModel;

    private void OnCardChanged()
    {
        if (Card is not { } card) return;

        // Плавное появление: лёгкое увеличение из 94%.
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var grow = new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        Glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        Glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        Glow.BlurRadius = card.IsHighRarity ? 56 : 40;
        Glow.Opacity = card.IsHighRarity ? 0.7 : 0.45;

        if (card.IsLegendary)
        {
            // Усиленное золотое свечение с медленной пульсацией.
            var blur = new DoubleAnimation(48, 90, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            var opacity = new DoubleAnimation(0.6, 1.0, TimeSpan.FromSeconds(1.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            Glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur);
            Glow.BeginAnimation(DropShadowEffect.OpacityProperty, opacity);
        }
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (Card is { } c) c.IsHovered = true; // пауза таймера
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (Card is { } c) c.IsHovered = false;
    }
}
