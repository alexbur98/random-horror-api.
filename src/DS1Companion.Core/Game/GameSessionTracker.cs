using DS1Companion.Core.Data;
using DS1Companion.Core.Logic;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Game;

public sealed record ItemPickup(ItemInfo Item, int Quantity, DateTime At);
public sealed record BossFightResult(BossInfo Boss, bool Victory, TimeSpan Duration);

/// <summary>
/// Превращает поток снимков в события: подбор предметов (diff инвентаря), смерти, кровавое пятно,
/// смена локации, низкая прочность оружия, начало/конец боя с боссом. Чистая логика — без UI и потоков.
/// </summary>
public sealed class GameSessionTracker
{
    public const double LowDurabilityThreshold = 0.30;

    private readonly KnowledgeBase _kb;
    private GameSnapshot? _last;
    private bool _durabilityWarned;
    private int? _heuristicBloodstain;
    private DateTime? _bossFightStart;
    private int _deathsAtBossStart;
    private BossInfo? _currentBoss;

    public event Action<ItemPickup>? ItemPickedUp;
    public event Action<int>? Died; // аргумент — души, ушедшие в пятно
    public event Action<int>? BloodstainRecovered;
    public event Action<LocationInfo?>? LocationChanged;
    public event Action<EquippedWeapon>? WeaponDurabilityLow;
    public event Action<BossInfo>? BossFightStarted;
    public event Action<BossFightResult>? BossFightEnded;
    public event Action<string>? CharacterChanged;

    public GameSnapshot? Last => _last;
    public int? SessionStartDeaths { get; private set; }
    public int SessionDeaths => _last?.Deaths is { } d && SessionStartDeaths is { } s ? Math.Max(0, d - s) : 0;
    public int ItemsPickedThisSession { get; private set; }
    public LocationInfo? CurrentLocation { get; private set; }
    public BossInfo? CurrentBoss => _currentBoss;

    /// <summary>Души в кровавом пятне: из памяти, если доступно, иначе по эвристике.</summary>
    public int BloodstainSouls => _last?.BloodstainSouls ?? _heuristicBloodstain ?? 0;

    public GameSessionTracker(KnowledgeBase kb) => _kb = kb;

    public KnowledgeBase Knowledge => _kb;

    public void Reset()
    {
        _last = null;
        _durabilityWarned = false;
        _heuristicBloodstain = null;
        _bossFightStart = null;
        _currentBoss = null;
        SessionStartDeaths = null;
        ItemsPickedThisSession = 0;
        CurrentLocation = null;
    }

    public void Process(GameSnapshot s, DateTime now)
    {
        if (!s.IsConnected)
        {
            // Игра закрыта — сохраняем базовую линию сессии, но следующий снимок станет новой «точкой отсчёта» инвентаря.
            if (_last is not null) _last = _last with { IsConnected = false, Inventory = null };
            return;
        }

        var prev = _last;
        if (prev is not null && prev.IsConnected && s.CharacterName != prev.CharacterName && prev.CharacterName is not null)
        {
            Reset();
            prev = null;
            if (s.CharacterName is not null) CharacterChanged?.Invoke(s.CharacterName);
        }
        else if (prev is null && s.CharacterName is not null)
        {
            CharacterChanged?.Invoke(s.CharacterName);
        }

        SessionStartDeaths ??= s.Deaths;
        _last = s;

        DetectLocation(s);
        if (prev is null || !prev.IsConnected || prev.Inventory is null)
        {
            // Первый снимок — только база для сравнения, «подборов» нет.
            _durabilityWarned = false;
            return;
        }

        DetectDeath(prev, s);
        DetectBloodstainRecovery(prev, s);
        DetectPickups(prev, s, now);
        DetectDurability(prev, s);
        DetectBoss(prev, s, now);
    }

    private void DetectLocation(GameSnapshot s)
    {
        var loc = _kb.ResolveLocation(s);
        if (loc?.Id == CurrentLocation?.Id) return;
        CurrentLocation = loc;
        LocationChanged?.Invoke(loc);
    }

    private void DetectDeath(GameSnapshot prev, GameSnapshot s)
    {
        if (s.Deaths is not { } d || prev.Deaths is not { } pd || d <= pd) return;
        var lost = s.BloodstainSouls ?? prev.Souls ?? 0;
        if (s.BloodstainSouls is null) _heuristicBloodstain = lost; // старое пятно сгорает при новой смерти
        Died?.Invoke(lost);
    }

    private void DetectBloodstainRecovery(GameSnapshot prev, GameSnapshot s)
    {
        if (s.Deaths != prev.Deaths) return;
        if (s.BloodstainSouls is { } now && prev.BloodstainSouls is { } before)
        {
            if (before > 0 && now == 0) BloodstainRecovered?.Invoke(before);
            return;
        }
        // Эвристика: души разом выросли не меньше чем на сумму пятна.
        if (_heuristicBloodstain is > 0 and var stain && (s.Souls ?? 0) - (prev.Souls ?? 0) >= stain)
        {
            _heuristicBloodstain = 0;
            BloodstainRecovered?.Invoke(stain);
        }
    }

    private void DetectPickups(GameSnapshot prev, GameSnapshot s, DateTime now)
    {
        foreach (var gain in InventoryDiff.Compute(prev.Inventory, s.Inventory))
        {
            var item = _kb.ResolveInventoryItem(gain.Kind, gain.GameId, gain.ItemId);
            ItemsPickedThisSession++;
            ItemPickedUp?.Invoke(new ItemPickup(item, gain.Quantity, now));
        }
    }

    private void DetectDurability(GameSnapshot prev, GameSnapshot s)
    {
        if (s.Weapon is not { } w) { _durabilityWarned = false; return; }
        if (prev.Weapon?.BaseId != w.BaseId) _durabilityWarned = false; // сменили оружие — оцениваем заново

        if (w.DurabilityRatio >= LowDurabilityThreshold) { _durabilityWarned = false; return; }
        if (_durabilityWarned) return;
        _durabilityWarned = true;
        WeaponDurabilityLow?.Invoke(w);
    }

    private void DetectBoss(GameSnapshot prev, GameSnapshot s, DateTime now)
    {
        if (s.InBossFight && !prev.InBossFight)
        {
            _currentBoss = (s.BossId is { } id ? _kb.FindBoss(id) : null)
                           ?? (CurrentLocation is { } loc ? _kb.BossesIn(loc.Id).FirstOrDefault() : null);
            if (_currentBoss is null) return;
            _bossFightStart = now;
            _deathsAtBossStart = s.Deaths ?? 0;
            BossFightStarted?.Invoke(_currentBoss);
        }
        else if (!s.InBossFight && prev.InBossFight && _currentBoss is { } boss && _bossFightStart is { } start)
        {
            var victory = (s.Deaths ?? 0) <= _deathsAtBossStart;
            _currentBoss = null;
            _bossFightStart = null;
            BossFightEnded?.Invoke(new BossFightResult(boss, victory, now - start));
        }
    }
}
