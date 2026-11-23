namespace DS1Companion.Core.Models;

public sealed record CharacterStats
{
    public int Vitality { get; init; }
    public int Attunement { get; init; }
    public int Endurance { get; init; }
    public int Strength { get; init; }
    public int Dexterity { get; init; }
    public int Resistance { get; init; }
    public int Intelligence { get; init; }
    public int Faith { get; init; }
}

public readonly record struct InventoryEntry(ItemKind Kind, int GameId, int Quantity);

public sealed record EquippedWeapon
{
    /// <summary>Сырой ID из памяти (база + инфузия*100 + заточка).</summary>
    public int RawId { get; init; }
    public int Durability { get; init; }
    public int MaxDurability { get; init; }

    public int BaseId => WeaponIdCodec.BaseId(RawId);
    public int UpgradeLevel => WeaponIdCodec.UpgradeLevel(RawId);
    public int Infusion => WeaponIdCodec.Infusion(RawId);

    public double DurabilityRatio => MaxDurability <= 0 ? 1.0 : Math.Clamp((double)Durability / MaxDurability, 0, 1);
}

/// <summary>Мгновенный снимок состояния игры. Любое поле может быть null, если его не удалось прочитать.</summary>
public sealed record GameSnapshot
{
    public bool IsConnected { get; init; }
    public string? CharacterName { get; init; }
    public int? Deaths { get; init; }
    public int? Area { get; init; }
    public int? Block { get; init; }
    /// <summary>Прямой ID локации (используется демо-режимом, у памяти приоритет за Area/Block).</summary>
    public string? LocationId { get; init; }
    public int? Souls { get; init; }
    public int? Level { get; init; }
    public CharacterStats? Stats { get; init; }
    public EquippedWeapon? Weapon { get; init; }
    /// <summary>Души в кровавом пятне, если удалось прочитать напрямую. null — использовать эвристику трекера.</summary>
    public int? BloodstainSouls { get; init; }
    public IReadOnlyList<InventoryEntry>? Inventory { get; init; }
    public bool InBossFight { get; init; }
    public string? BossId { get; init; }

    public static GameSnapshot Disconnected { get; } = new() { IsConnected = false };
}

public static class WeaponIdCodec
{
    public static int UpgradeLevel(int rawId) => rawId < 0 ? 0 : rawId % 100;
    public static int Infusion(int rawId) => rawId < 0 ? 0 : rawId % 1000 / 100;
    public static int BaseId(int rawId) => rawId < 0 ? rawId : rawId - rawId % 1000;
    public static int Compose(int baseId, int infusion, int level) => baseId + infusion * 100 + level;

    public static string InfusionNameRu(int infusion) => infusion switch
    {
        1 => "Кристальный",
        2 => "Молниевый",
        3 => "Острый",
        4 => "Магический",
        5 => "Зачарованный",
        6 => "Божественный",
        7 => "Оккультный",
        8 => "Огненный",
        9 => "Хаоса",
        _ => "",
    };
}
