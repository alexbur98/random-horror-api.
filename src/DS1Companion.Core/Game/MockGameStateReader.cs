using DS1Companion.Core.Data;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Game;

/// <summary>Встроенный демо-режим: изменяемое состояние «игры» для проверки всего UI без DSR.</summary>
public sealed class MockGameStateReader : IGameStateReader
{
    private readonly object _gate = new();
    private readonly KnowledgeBase _kb;
    private readonly Random _random;
    private readonly List<InventoryEntry> _inventory = new();
    private GameSnapshot _state;
    private int _locationIndex;

    public string Name => "Демо";

    public MockGameStateReader(KnowledgeBase kb, int seed = 42)
    {
        _kb = kb;
        _random = new Random(seed);
        _locationIndex = Math.Max(0, kb.Locations.ToList().FindIndex(l => l.Id == "undead_burg"));
        var claymore = kb.FindItem("claymore")?.GameId ?? 301000;

        _inventory.Add(new InventoryEntry(ItemKind.Goods, kb.FindItem("estus_flask")?.GameId ?? 200, 5));
        _inventory.Add(new InventoryEntry(ItemKind.Weapon, claymore + 5, 1));

        _state = new GameSnapshot
        {
            IsConnected = true,
            CharacterName = "Избранная нежить",
            Deaths = 37,
            LocationId = kb.Locations.Count > 0 ? kb.Locations[_locationIndex].Id : null,
            Souls = 4_210,
            Level = 24,
            Stats = new CharacterStats
            {
                Vitality = 18, Attunement = 10, Endurance = 22, Strength = 20,
                Dexterity = 14, Resistance = 11, Intelligence = 9, Faith = 9,
            },
            Weapon = new EquippedWeapon { RawId = claymore + 5, Durability = 380, MaxDurability = 400 },
            Inventory = _inventory.ToList(),
        };
    }

    public GameSnapshot Read()
    {
        lock (_gate) return _state with { Inventory = _inventory.ToList() };
    }

    private void Update(Func<GameSnapshot, GameSnapshot> change)
    {
        lock (_gate) _state = change(_state);
    }

    public ItemInfo? PickUp(string itemId, int quantity = 1)
    {
        var item = _kb.FindItem(itemId);
        if (item?.GameKind is not { } kind || item.GameId is not { } gid) return null;
        lock (_gate)
        {
            var idx = _inventory.FindIndex(e => e.Kind == kind && e.GameId == gid);
            if (idx >= 0 && kind == ItemKind.Goods)
                _inventory[idx] = _inventory[idx] with { Quantity = _inventory[idx].Quantity + quantity };
            else
                _inventory.Add(new InventoryEntry(kind, gid, quantity));
        }
        return item;
    }

    public ItemInfo? PickUpRandom(Rarity rarity)
    {
        var pool = _kb.Items.Where(i => i.Rarity == rarity && i.GameId is not null && i.GameKind is not null).ToList();
        if (pool.Count == 0) return null;
        return PickUp(pool[_random.Next(pool.Count)].Id);
    }

    public void AddSouls(int amount) => Update(s => s with { Souls = Math.Max(0, (s.Souls ?? 0) + amount) });

    /// <summary>Смерть: души на руках уходят в кровавое пятно (старое пятно сгорает).</summary>
    public void Die() => Update(s => s with
    {
        Deaths = (s.Deaths ?? 0) + 1,
        BloodstainSouls = s.Souls ?? 0,
        Souls = 0,
        InBossFight = false,
    });

    public void RecoverBloodstain() => Update(s => s with
    {
        Souls = (s.Souls ?? 0) + (s.BloodstainSouls ?? 0),
        BloodstainSouls = 0,
    });

    public void DamageWeapon(double fraction = 0.25) => Update(s => s.Weapon is null ? s : s with
    {
        Weapon = s.Weapon with { Durability = Math.Max(0, s.Weapon.Durability - (int)(s.Weapon.MaxDurability * fraction)) },
    });

    public void RepairWeapon() => Update(s => s.Weapon is null ? s : s with
    {
        Weapon = s.Weapon with { Durability = s.Weapon.MaxDurability },
    });

    public void UpgradeWeapon() => Update(s => s.Weapon is null ? s : s with
    {
        Weapon = s.Weapon with { RawId = s.Weapon.UpgradeLevel >= 15 ? s.Weapon.BaseId : s.Weapon.RawId + 1 },
    });

    public void LevelUp()
    {
        Update(s =>
        {
            var level = s.Level ?? 1;
            var cost = Logic.SoulsCalculator.CostForNextLevel(level);
            if ((s.Souls ?? 0) < cost) return s;
            var st = s.Stats ?? new CharacterStats();
            return s with { Level = level + 1, Souls = s.Souls - cost, Stats = st with { Vitality = st.Vitality + 1 } };
        });
    }

    public LocationInfo? NextLocation()
    {
        if (_kb.Locations.Count == 0) return null;
        _locationIndex = (_locationIndex + 1) % _kb.Locations.Count;
        var loc = _kb.Locations[_locationIndex];
        Update(s => s with { LocationId = loc.Id, InBossFight = false });
        return loc;
    }

    public BossInfo? StartBossFight()
    {
        var current = Read().LocationId;
        var boss = (current is null ? null : _kb.BossesIn(current).FirstOrDefault()) ?? _kb.Bosses.FirstOrDefault();
        if (boss is null) return null;
        Update(s => s with { InBossFight = true, BossId = boss.Id });
        return boss;
    }

    public void EndBossFight(bool victory)
    {
        if (victory) Update(s => s with { InBossFight = false, Souls = (s.Souls ?? 0) + 3_000 });
        else Die();
    }

    public void Dispose() { }
}
