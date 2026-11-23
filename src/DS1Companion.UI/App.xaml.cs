using System.IO;
using System.Windows;
using System.Windows.Threading;
using DS1Companion.Core.Data;
using DS1Companion.Core.Game;
using DS1Companion.Core.Memory;
using DS1Companion.Core.Persistence;
using DS1Companion.Core.Presentation;
using DS1Companion.Core.Services;
using DS1Companion.UI.Services;
using DS1Companion.UI.Views;

namespace DS1Companion.UI;

/// <summary>Composition root: собирает сервисы Core и окно.</summary>
public partial class App : Application
{
    private MainViewModel? _vm;
    private GamePollingService? _poller;
    private CachingIconProvider? _icons;
    private FileLogger? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureCreated();
        _log = new FileLogger(paths.LogsDir, "app.log");
        DispatcherUnhandledException += OnUnhandled;

        var baseDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "Data");
        KnowledgeBase kb;
        try
        {
            kb = KnowledgeBase.LoadFromDirectory(dataDir);
        }
        catch (Exception ex)
        {
            _log.Write($"Не удалось загрузить базу знаний: {ex}");
            MessageBox.Show($"Не удалось загрузить базу знаний из {dataDir}:\n{ex.Message}", "DS1 Companion",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var settingsStore = new SettingsStore(paths);
        var settings = settingsStore.Load();

        var localIcons = Path.Combine(baseDir, "Assets", "Icons");
        var cacheDir = IsWritable(localIcons) ? localIcons : paths.IconCacheDir;
        _icons = new CachingIconProvider(new[] { localIcons, paths.IconCacheDir }.Distinct().ToList(), cacheDir,
            post: a => Dispatcher.BeginInvoke(a));

        OffsetsConfig? offsets = null;
        try
        {
            offsets = OffsetsConfig.Load(Path.Combine(dataDir, "offsets.json"));
            foreach (var err in offsets.Validate()) _log.Write($"offsets.json: {err}");
        }
        catch (Exception ex)
        {
            _log.Write($"offsets.json не загружен: {ex.Message}");
        }

        var memoryLog = new FileLogger(paths.LogsDir, "memory.log");
        IGameStateReader CreateLive()
        {
            if (offsets is null) return new DisconnectedReader();
            var reader = new DsrMemoryReader(offsets);
            reader.Log += memoryLog.Write;
            return reader;
        }

        _vm = new MainViewModel(new MainViewModelDependencies
        {
            Knowledge = kb,
            Sound = new WpfSoundService(),
            Icons = _icons,
            Settings = settings,
            SaveSettings = settingsStore.Save,
            Profiles = new ProfileStore(paths),
            Clock = new SystemClock(),
            CreateLiveReader = CreateLive,
        });

        var window = new MainWindow(_vm, settings);
        MainWindow = window;
        _poller = new GamePollingService(_vm, Dispatcher, offsets?.PollIntervalMs ?? 250);
        _poller.Start();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _poller?.Dispose();
        _vm?.Shutdown();
        _vm?.Dispose();
        _icons?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Write($"Необработанная ошибка: {e.Exception}");
        e.Handled = true; // компаньон не должен падать посреди игры
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write_probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
