using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DS1Companion.Core.Data;
using DS1Companion.Core.Game;
using DS1Companion.Core.Logic;
using DS1Companion.Core.Models;
using DS1Companion.Core.Persistence;
using DS1Companion.Core.Services;

namespace DS1Companion.Core.Presentation;

public sealed class MainViewModelDependencies
{
    public required GameCatalog Catalog { get; init; }
    public required ISoundService Sound { get; init; }
    public required IIconProvider Icons { get; init; }
    public required AppSettings Settings { get; init; }
    public required Action<AppSettings> SaveSettings { get; init; }
    public required ProfileStore Profiles { get; init; }
    public required IClock Clock { get; init; }
    /// <summary>Ридер живой игры (обычно <see cref="MultiGameReader"/> с автоопределением).</summary>
    public required Func<IGameStateReader> CreateLiveReader { get; init; }
}

public sealed record GameOption(GameKey Key, string Title, string ShortTitle);

/// <summary>
/// Главная ViewModel. Два состояния экрана: «Спокойный режим» и «Карточка подбора».
/// Поддерживает несколько игр: термины, механики и данные берутся из базы знаний текущей игры.
/// Все методы вызываются из UI-потока; время подаётся через <see cref="Tick"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const int MaxRecentLoot = 4;
    private static readonly TimeSpan SettingsSaveDelay = TimeSpan.FromSeconds(1);

    private readonly MainViewModelDependencies _d;
    private readonly Queue<ItemCardViewModel> _cardQueue = new();
    private KnowledgeBase _kb;
    private GameSessionTracker _tracker;
    private UpgradeAdvisor _upgrades;
    private CharacterProfile? _profile;
    private DateTime _sessionStartedAt;
    private TimeSpan _sessionElapsed;
    private TimeSpan? _settingsDirtyFor;
    private bool _suppressGameSelection;

    public ObservableCollection<string> Tips { get; } = new();
    public ObservableCollection<LootRowViewModel> RecentLoot { get; } = new();
    public ObservableCollection<StatRowViewModel> StatRows { get; } = new();
    public ObservableCollection<CollectedRowViewModel> Collection { get; } = new();
    public ObservableCollection<BossStatRowViewModel> BossStats { get; } = new();
    public WeaponPanelViewModel Weapon { get; } = new();
    public IReadOnlyList<Rarity> RarityOptions { get; } = Enum.GetValues<Rarity>();
    public IReadOnlyList<GameOption> GameOptions { get; }

    public IGameStateReader CurrentReader { get; private set; }

    [ObservableProperty] private ConnectionState _connectionState = ConnectionState.Waiting;
    [ObservableProperty] private string _connectionLabel = "Ожидание игры";
    [ObservableProperty] private string _sessionTime = "00:00:00";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _locationNameRu = "Неизвестная земля";
    [ObservableProperty] private string _locationNameEn = "Unknown";
    [ObservableProperty] private int _sessionDeaths;
    [ObservableProperty] private int _totalDeaths;
    [ObservableProperty] private int _level;
    [ObservableProperty] private string _soulsText = "—";
    [ObservableProperty] private string _soulsHint = "";
    [ObservableProperty] private double _soulsProgress;
    [ObservableProperty] private bool _showSoulsProgress;
    [ObservableProperty] private bool _hasBloodstain;
    [ObservableProperty] private string _bloodstainText = "";
    [ObservableProperty] private bool _showHollowing;
    [ObservableProperty] private string _hollowingText = "";
    [ObservableProperty] private bool _showDurabilityWarning;
    [ObservableProperty] private string _durabilityWarningText = "";
    [ObservableProperty] private bool _isInBossFight;

    // ── Термины текущей игры ──
    [ObservableProperty] private GameOption? _selectedGame;
    [ObservableProperty] private string _gameTitle = "";
    [ObservableProperty] private string _currencyCaption = "";
    [ObservableProperty] private string _bloodstainTitle = "";
    [ObservableProperty] private string _hollowingTitle = "";
    [ObservableProperty] private string _statsSheetHint = "";
    [ObservableProperty] private bool _hasWeaponPanel;
    [ObservableProperty] private GameTheme _theme = new();
    [ObservableProperty] private string _waitingHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCardVisible))]
    [NotifyPropertyChangedFor(nameof(PendingCardsText))]
    private ItemCardViewModel? _currentCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    private bool _isStatsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    private bool _isBossesOpen;

    [ObservableProperty] private string _collectionSummary = "";
    [ObservableProperty] private bool _isDemoMode;
    [ObservableProperty] private DemoPanelViewModel? _demo;
    [ObservableProperty] private double _volume;
    [ObservableProperty] private bool _isMuted;
    [ObservableProperty] private bool _isTopmost;
    [ObservableProperty] private Rarity _popupMinRarity;

    public bool IsCardVisible => CurrentCard is not null;
    public bool IsSheetOpen => IsStatsOpen || IsBossesOpen;
    public string PendingCardsText => _cardQueue.Count > 0 ? $"Ещё {_cardQueue.Count} в очереди" : "";
    public bool HasRecentLoot => RecentLoot.Count > 0;
    public CharacterProfile? Profile => _profile;
    public GameSessionTracker Tracker => _tracker;
    public KnowledgeBase Knowledge => _kb;
    public GameDefinition Game => _kb.Game;

    public MainViewModel(MainViewModelDependencies deps)
    {
        _d = deps;
        GameOptions = deps.Catalog.Games.Select(g => new GameOption(g.Game.Key, g.Game.TitleRu, g.Game.ShortTitle)).ToList();
        _kb = deps.Catalog.Find(deps.Settings.SelectedGame) ?? deps.Catalog.Default;
        _tracker = CreateTracker(_kb);
        _upgrades = new UpgradeAdvisor(_kb);
        _sessionStartedAt = deps.Clock.Now;

        _volume = deps.Settings.Volume;
        _isMuted = deps.Settings.IsMuted;
        _isTopmost = deps.Settings.IsTopmost;
        _popupMinRarity = deps.Settings.PopupMinRarity;
        deps.Sound.Volume = _volume;
        deps.Sound.IsMuted = _isMuted;
        deps.Icons.IconUpdated += OnIconUpdated;
        RecentLoot.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentLoot));

        CurrentReader = deps.Settings.StartInDemo ? CreateDemoReader() : deps.CreateLiveReader();
        IsDemoMode = CurrentReader is MockGameStateReader;
        ApplyGameTerms();
        ApplyConnection(false);
        RefreshBossStats();
        RefreshCollection();
        ShowDefaultTips();
    }

    // ───────────── Игры ─────────────

    private GameSessionTracker CreateTracker(KnowledgeBase kb)
    {
        var t = new GameSessionTracker(kb);
        t.ItemPickedUp += OnItemPickedUp;
        t.Died += OnDied;
        t.BloodstainRecovered += _ => RefreshBloodstain();
        t.LocationChanged += OnLocationChanged;
        t.WeaponDurabilityLow += OnDurabilityLow;
        t.BossFightStarted += OnBossStarted;
        t.BossFightEnded += OnBossEnded;
        t.CharacterChanged += OnCharacterChanged;
        return t;
    }

    private void ApplyGameTerms()
    {
        var g = _kb.Game;
        _suppressGameSelection = true;
        SelectedGame = GameOptions.FirstOrDefault(o => o.Key == g.Key);
        _suppressGameSelection = false;
        GameTitle = g.TitleRu;
        CurrencyCaption = g.HasLevels
            ? $"{g.CurrencyRu.ToUpperInvariant()} НА РУКАХ / НА СЛЕДУЮЩИЙ УРОВЕНЬ"
            : $"{g.CurrencyRu.ToUpperInvariant()} НА РУКАХ";
        BloodstainTitle = g.BloodstainTitleRu.ToUpperInvariant();
        HollowingTitle = g.HollowingTitleRu.ToUpperInvariant();
        StatsSheetHint = g.StatsSheetHintRu;
        HasWeaponPanel = g.HasWeaponUpgrades;
        Theme = g.Theme;
        WaitingHint = g.MemoryNoteRu;
        ShowSoulsProgress = false;
        SoulsText = "—";
        SoulsHint = "";
        Level = 0;
        ShowHollowing = false;
        HasBloodstain = false;
        ShowDurabilityWarning = false;
        Weapon.Update(null, null);
    }

    /// <summary>Переключиться на другую игру: свой трекер, база знаний, профили и демо.</summary>
    private void SwitchGame(KnowledgeBase kb)
    {
        if (ReferenceEquals(kb, _kb)) return;
        EndProfileSession();
        _profile = null;
        _kb = kb;
        _tracker = CreateTracker(kb);
        _upgrades = new UpgradeAdvisor(kb);
        _cardQueue.Clear();
        CurrentCard = null;
        RecentLoot.Clear();
        StatRows.Clear();
        ApplyGameTerms();
        LocationNameRu = "Неизвестная земля";
        LocationNameEn = "Unknown";
        ShowDefaultTips();
        RefreshBossStats();
        RefreshCollection();
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(Knowledge));
    }

    partial void OnSelectedGameChanged(GameOption? value)
    {
        if (_suppressGameSelection || value is null || value.Key == _kb.Game.Key) return;
        _d.Settings.SelectedGame = value.Key;
        MarkSettingsDirty();
        SwitchGame(_d.Catalog.Get(value.Key));
        if (IsDemoMode)
        {
            var old = CurrentReader;
            CurrentReader = CreateDemoReader();
            old.Dispose();
            _sessionElapsed = TimeSpan.Zero;
        }
    }

    // ───────────── Входящие данные ─────────────

    /// <summary>Снимок от ридера. Снимки устаревшего ридера (после переключения режима) игнорируются.</summary>
    public void OnSnapshot(IGameStateReader source, GameSnapshot snapshot)
    {
        if (!ReferenceEquals(source, CurrentReader)) return;
        if (snapshot.IsConnected && snapshot.Game is { } key && key != _kb.Game.Key && _d.Catalog.Find(key) is { } detected)
            SwitchGame(detected); // автоопределение запущенной игры

        _tracker.Process(snapshot, _d.Clock.Now);
        ApplyConnection(snapshot.IsConnected);
        if (!snapshot.IsConnected) return;
        // Имя персонажа не прочиталось (другая версия игры) — ведём статистику в общем профиле.
        if (_profile is null) OnCharacterChanged(snapshot.CharacterName ?? "Безымянный");

        var g = _kb.Game;
        CharacterName = snapshot.CharacterName ?? "";
        if (snapshot.Deaths is { } deaths)
        {
            TotalDeaths = deaths;
            SessionDeaths = _tracker.SessionDeaths;
            if (_profile is not null) _profile.LastKnownDeaths = deaths;
        }

        UpdateSouls(g, snapshot);
        IsInBossFight = snapshot.InBossFight;
        Weapon.Update(snapshot.Weapon, _upgrades.Advise(snapshot.Weapon));
        UpdateDurabilityWarning(snapshot.Weapon);
        RefreshBloodstain();
        UpdateHollowing(g, snapshot);
        if (snapshot.Stats is { } stats) RefreshStats(stats);
    }

    private void UpdateSouls(GameDefinition g, GameSnapshot s)
    {
        var souls = s.Souls ?? 0;
        var currency = g.CurrencyGenitiveRu;
        if (!g.HasLevels || s.Level is not { } level)
        {
            ShowSoulsProgress = false;
            SoulsText = s.Souls is null ? "—" : $"{DisplayText.FormatSouls(souls)} {currency}";
            SoulsHint = "";
            return;
        }

        Level = level;
        var cost = s.SoulsForNextLevel ?? LevelCurve.CostForNextLevel(g, level);
        if (cost is not { } c || c <= 0)
        {
            ShowSoulsProgress = false;
            SoulsText = DisplayText.FormatSouls(souls);
            SoulsHint = "Стоимость следующего уровня неизвестна для этой версии игры.";
            return;
        }

        ShowSoulsProgress = true;
        SoulsText = $"{DisplayText.FormatSouls(souls)} / {DisplayText.FormatSouls(c)}";
        SoulsProgress = Math.Clamp((double)souls / c, 0, 1);
        var affordable = s.SoulsForNextLevel is null ? LevelCurve.AffordableLevels(g, souls, level) : souls >= c ? 1 : 0;
        SoulsHint = affordable > 0
            ? (s.SoulsForNextLevel is null
                ? $"Хватает на {affordable} {Plural(affordable, "уровень", "уровня", "уровней")} — отдохните у костра"
                : "Хватает на новый уровень — отдохните у костра")
            : $"До уровня {level + 1}: {DisplayText.FormatSouls(Math.Max(0, c - souls))} {currency}";
    }

    private void UpdateHollowing(GameDefinition g, GameSnapshot s)
    {
        ShowHollowing = g.HasHollowing && s.HollowLevel is > 0;
        HollowingText = ShowHollowing ? string.Format(g.HollowingTextRu, s.HollowLevel) : "";
    }

    /// <summary>Тик UI-таймера: обратный отсчёт карточки, таймер сессии, отложенное сохранение настроек.</summary>
    public void Tick(TimeSpan delta)
    {
        if (ConnectionState != ConnectionState.Waiting) _sessionElapsed += delta;
        SessionTime = DisplayText.FormatDuration(_sessionElapsed);

        CurrentCard?.Advance(delta); // по истечении сработает Countdown.Expired → CloseCurrentCard

        if (_settingsDirtyFor is { } dirty)
        {
            dirty += delta;
            if (dirty >= SettingsSaveDelay) FlushSettings();
            else _settingsDirtyFor = dirty;
        }
    }

    // ───────────── Карточки ─────────────

    private ItemCardViewModel NewCard(ItemInfo item, int qty, DateTime at, bool replay) =>
        new(item, _d.Icons.Resolve(item), qty, at, TimeSpan.FromSeconds(Math.Clamp(_d.Settings.PopupSeconds, 3, 60)), replay)
        {
            Game = _kb.Game,
        };

    private void OnItemPickedUp(ItemPickup pickup)
    {
        var item = pickup.Item;
        if (RarityFilter.IsCollectible(item) && _profile is not null && _profile.AddToCollection(item.Id, pickup.At))
            RefreshCollection();

        if (!RarityFilter.ShouldShowPopup(item, PopupMinRarity))
        {
            AddRecent(item, pickup.Quantity, pickup.At);
            return;
        }

        var card = NewCard(item, pickup.Quantity, pickup.At, replay: false);
        if (CurrentCard is null) ShowCard(card);
        else
        {
            _cardQueue.Enqueue(card);
            OnPropertyChanged(nameof(PendingCardsText));
        }
    }

    private void ShowCard(ItemCardViewModel card)
    {
        CurrentCard = card;
        if (!card.IsReplay && RarityFilter.CueFor(card.Rarity) is { } cue) _d.Sound.Play(cue);
    }

    private void CloseCurrentCard()
    {
        if (CurrentCard is not { } card) return;
        if (!card.IsReplay) AddRecent(card.Item, card.Quantity, card.PickedAt);
        CurrentCard = null;
        if (_cardQueue.Count > 0) ShowCard(_cardQueue.Dequeue());
        OnPropertyChanged(nameof(PendingCardsText));
    }

    private void AddRecent(ItemInfo item, int quantity, DateTime at)
    {
        RecentLoot.Insert(0, new LootRowViewModel(item, _d.Icons.Resolve(item), quantity, at));
        while (RecentLoot.Count > MaxRecentLoot) RecentLoot.RemoveAt(RecentLoot.Count - 1);
    }

    [RelayCommand]
    private void DismissCard() => CurrentCard?.Countdown.Complete();

    partial void OnCurrentCardChanged(ItemCardViewModel? oldValue, ItemCardViewModel? newValue)
    {
        if (oldValue is not null) oldValue.Countdown.Expired -= OnCardExpired;
        if (newValue is not null) newValue.Countdown.Expired += OnCardExpired;
    }

    /// <summary>Единая точка закрытия: и по таймеру (Advance), и по кнопке «закрыть» (Complete).</summary>
    private void OnCardExpired(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, CurrentCard?.Countdown)) CloseCurrentCard();
    }

    [RelayCommand]
    private void OpenLoot(LootRowViewModel? row)
    {
        if (row is null) return;
        var card = NewCard(row.Item, row.Quantity, row.PickedAt, replay: true);
        if (CurrentCard is null) ShowCard(card);
        else
        {
            _cardQueue.Enqueue(card);
            OnPropertyChanged(nameof(PendingCardsText));
        }
    }

    private void OnIconUpdated(string itemId)
    {
        if (CurrentCard is { } c && c.Item.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase))
            c.Icon = _d.Icons.Resolve(c.Item);
        foreach (var row in RecentLoot.Where(r => r.Item.Id.Equals(itemId, StringComparison.OrdinalIgnoreCase)))
            row.Icon = _d.Icons.Resolve(row.Item);
    }

    // ───────────── События игры ─────────────

    private void OnDied(int soulsLost) => RefreshBloodstain();

    private void RefreshBloodstain()
    {
        var g = _kb.Game;
        var stain = g.HasBloodstain ? _tracker.BloodstainSouls : 0;
        HasBloodstain = stain > 0;
        BloodstainText = stain > 0 ? string.Format(g.BloodstainTextRu, DisplayText.FormatSouls(stain), g.CurrencyGenitiveRu) : "";
    }

    private void UpdateDurabilityWarning(EquippedWeapon? w)
    {
        ShowDurabilityWarning = _kb.Game.HasDurability && w is { MaxDurability: > 0 }
                                && w.DurabilityRatio < GameSessionTracker.LowDurabilityThreshold;
        DurabilityWarningText = ShowDurabilityWarning
            ? $"Прочность оружия {Math.Round(w!.DurabilityRatio * 100)}% — {_kb.Game.RepairHintRu}."
            : "";
    }

    private void OnDurabilityLow(EquippedWeapon w)
    {
        if (_kb.Game.HasDurability) _d.Sound.Play(SoundCue.DurabilityWarning);
    }

    private void OnLocationChanged(LocationInfo? loc)
    {
        if (loc is null)
        {
            LocationNameRu = "Неизвестная земля";
            LocationNameEn = "Unknown";
            ShowDefaultTips();
            return;
        }
        LocationNameRu = loc.NameRu;
        LocationNameEn = loc.NameEn;
        Tips.Clear();
        foreach (var t in loc.TipsRu.Take(2)) Tips.Add(t);
        if (Tips.Count == 0) ShowDefaultTips();
    }

    private void ShowDefaultTips()
    {
        Tips.Clear();
        Tips.Add(string.IsNullOrWhiteSpace(_kb.Game.DefaultTipRu)
            ? "Отдыхайте у костров: это восполняет лечение, но возрождает обычных врагов."
            : _kb.Game.DefaultTipRu);
    }

    private void OnBossStarted(BossInfo boss)
    {
        if (_profile is null) return;
        BossStatsRecorder.RecordStart(_profile, boss.Id);
        SaveProfile();
        RefreshBossStats();
    }

    private void OnBossEnded(BossFightResult result)
    {
        if (_profile is null) return;
        BossStatsRecorder.RecordEnd(_profile, result, _d.Clock.Now);
        SaveProfile();
        RefreshBossStats();
    }

    private void OnCharacterChanged(string name)
    {
        EndProfileSession();
        _profile = _d.Profiles.Load(_kb.Game.Key, IsDemoMode ? $"demo_{name}" : name);
        _sessionStartedAt = _d.Clock.Now;
        RefreshCollection();
        RefreshBossStats();
        OnPropertyChanged(nameof(Profile));
    }

    private void EndProfileSession()
    {
        if (_profile is null) return;
        _profile.Sessions.Add(new SessionRecord
        {
            StartedAt = _sessionStartedAt,
            DurationSeconds = Math.Round((_d.Clock.Now - _sessionStartedAt).TotalSeconds),
            Deaths = _tracker.SessionDeaths,
            ItemsPicked = _tracker.ItemsPickedThisSession,
        });
        if (_profile.Sessions.Count > 200) _profile.Sessions.RemoveRange(0, _profile.Sessions.Count - 200);
        _profile.TotalPlaySeconds += (_d.Clock.Now - _sessionStartedAt).TotalSeconds;
        SaveProfile();
    }

    private void SaveProfile()
    {
        if (_profile is null) return;
        try { _d.Profiles.Save(_profile); } catch (IOException) { /* диск занят — сохраним в следующий раз */ }
    }

    // ───────────── Окна «у костра» ─────────────

    private CharacterStats? _lastStats;

    private void RefreshStats(CharacterStats stats)
    {
        if (stats.SameAs(_lastStats) && StatRows.Count > 0) return;
        _lastStats = stats;
        StatRows.Clear();
        foreach (var h in SoftCapAdvisor.Analyze(_kb.Game, stats))
            StatRows.Add(new StatRowViewModel(h.NameRu, h.Value, h.HintRu, h.Advice));
    }

    private void RefreshCollection()
    {
        Collection.Clear();
        var total = _kb.Items.Count(RarityFilter.IsCollectible);
        if (_profile is null)
        {
            CollectionSummary = $"Коллекция редкого лута: 0 / {total}";
            return;
        }
        foreach (var c in _profile.RareCollection.Values.OrderByDescending(c => c.FirstFoundAt).Take(30))
        {
            var item = _kb.FindItem(c.ItemId);
            if (item is null) continue;
            Collection.Add(new CollectedRowViewModel(item.NameRu, DisplayText.RarityColorHex(item.Rarity),
                c.FirstFoundAt.ToString("dd.MM HH:mm")));
        }
        var found = _profile.RareCollection.Keys.Count(id => _kb.FindItem(id) is { } i && RarityFilter.IsCollectible(i));
        CollectionSummary = $"Коллекция редкого лута: {found} / {total}";
    }

    private void RefreshBossStats()
    {
        BossStats.Clear();
        var order = _kb.Bosses.Select((b, i) => (b, i)).ToDictionary(x => x.b.Id, x => x.i);
        var ordered = _kb.Bosses
            .Select(b => (Boss: b, Rec: _profile?.Bosses.GetValueOrDefault(b.Id)))
            .OrderByDescending(x => x.Rec?.Attempts > 0)
            .ThenBy(x => order[x.Boss.Id]);
        foreach (var (boss, rec) in ordered)
        {
            var attempts = rec?.Attempts ?? 0;
            var time = rec is null ? "—" : DisplayText.FormatDuration(TimeSpan.FromSeconds(rec.TotalSeconds));
            var status = rec switch
            {
                { Defeated: true, BestVictorySeconds: { } best } => $"Повержен · лучший бой {DisplayText.FormatDuration(TimeSpan.FromSeconds(best))}",
                { Defeated: true } => "Повержен",
                { Attempts: > 0 } => $"Поражений: {rec.Defeats}",
                _ => boss.Optional ? "Необязательный" : "Ещё не встречен",
            };
            BossStats.Add(new BossStatRowViewModel(boss.NameRu, boss.NameEn, attempts, time, rec?.Defeated == true, status));
        }
    }

    [RelayCommand] private void OpenStats() { IsBossesOpen = false; IsStatsOpen = true; }
    [RelayCommand] private void OpenBosses() { IsStatsOpen = false; IsBossesOpen = true; RefreshBossStats(); }
    [RelayCommand] private void CloseSheet() { IsStatsOpen = false; IsBossesOpen = false; }

    // ───────────── Режимы, звук, настройки ─────────────

    [RelayCommand]
    private void ToggleDemo()
    {
        EndProfileSession();
        _profile = null;
        var old = CurrentReader;
        if (old is MockGameStateReader) { Demo = null; CurrentReader = _d.CreateLiveReader(); }
        else
        {
            // Демо открывается для игры, выбранной в переключателе.
            var kb = _d.Catalog.Find(_d.Settings.SelectedGame) ?? _kb;
            if (!ReferenceEquals(kb, _kb)) SwitchGame(kb);
            CurrentReader = CreateDemoReader();
        }
        old.Dispose();
        _tracker = CreateTracker(_kb);
        _cardQueue.Clear();
        CurrentCard = null;
        RecentLoot.Clear();
        StatRows.Clear();
        _lastStats = null;
        IsDemoMode = CurrentReader is MockGameStateReader;
        _sessionElapsed = TimeSpan.Zero;
        ApplyGameTerms();
        ApplyConnection(false);
        RefreshBossStats();
        RefreshCollection();
        _d.Settings.StartInDemo = IsDemoMode;
        MarkSettingsDirty();
    }

    private MockGameStateReader CreateDemoReader()
    {
        var mock = new MockGameStateReader(_kb, seed: Environment.TickCount);
        var legendary = _kb.Game.Demo.LegendaryItemId is { } id ? _kb.FindItem(id)?.NameRu : null;
        Demo = new DemoPanelViewModel(mock, legendary);
        _tracker = CreateTracker(_kb);
        _lastStats = null;
        return mock;
    }

    private void ApplyConnection(bool connected)
    {
        ConnectionState = IsDemoMode ? ConnectionState.Demo : connected ? ConnectionState.Connected : ConnectionState.Waiting;
        ConnectionLabel = ConnectionState switch
        {
            ConnectionState.Demo => $"Демо · {_kb.Game.ShortTitle}",
            ConnectionState.Connected => _kb.Game.ShortTitle,
            _ => "Ожидание игры",
        };
    }

    [RelayCommand] private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnVolumeChanged(double value)
    {
        _d.Sound.Volume = Math.Clamp(value, 0, 1);
        _d.Settings.Volume = _d.Sound.Volume;
        MarkSettingsDirty();
    }

    partial void OnIsMutedChanged(bool value)
    {
        _d.Sound.IsMuted = value;
        _d.Settings.IsMuted = value;
        MarkSettingsDirty();
    }

    partial void OnIsTopmostChanged(bool value)
    {
        _d.Settings.IsTopmost = value;
        MarkSettingsDirty();
    }

    partial void OnPopupMinRarityChanged(Rarity value)
    {
        _d.Settings.PopupMinRarity = value;
        MarkSettingsDirty();
    }

    public void MarkSettingsDirty() => _settingsDirtyFor ??= TimeSpan.Zero;

    public void FlushSettings()
    {
        _settingsDirtyFor = null;
        try { _d.SaveSettings(_d.Settings); } catch (IOException) { /* повторим позже */ }
    }

    public void Shutdown()
    {
        EndProfileSession();
        FlushSettings();
    }

    public void Dispose()
    {
        _d.Icons.IconUpdated -= OnIconUpdated;
        CurrentReader.Dispose();
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var m10 = n % 10; var m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 is >= 2 and <= 4 && m100 is < 12 or > 14) return few;
        return many;
    }
}
