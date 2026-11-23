using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DS1Companion.Core.Persistence;
using DS1Companion.Core.Presentation;
using DS1Companion.UI.Controls;
using DS1Companion.UI.Services;

namespace DS1Companion.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;

    public MainWindow(MainViewModel vm, AppSettings settings)
    {
        _vm = vm;
        _settings = settings;
        InitializeComponent();
        DataContext = vm;
        ThemeManager.Apply(Application.Current.Resources, vm.Theme);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Theme)) ThemeManager.Apply(Application.Current.Resources, vm.Theme);
        };
        NoiseLayer.Fill = NoiseTexture.Create();
        SourceInitialized += (_, _) => WindowPlacement.Restore(this, _settings);
        KeyDown += OnKeyDown;
        // Окно без системной рамки при развороте выходит за край экрана на толщину рамки — компенсируем.
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_vm.IsCardVisible) _vm.DismissCardCommand.Execute(null);
        else if (_vm.IsSheetOpen) _vm.CloseSheetCommand.Execute(null);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Dim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _vm.CloseSheetCommand.Execute(null);

    protected override void OnClosing(CancelEventArgs e)
    {
        WindowPlacement.Save(this, _settings);
        _vm.FlushSettings();
        base.OnClosing(e);
    }
}
