namespace DS1Companion.Core.Models;

public sealed record LocationInfo
{
    public required string Id { get; init; }
    public required string NameRu { get; init; }
    public required string NameEn { get; init; }
    /// <summary>Номер области карты (mAA_BB → AA); 0 — локация распознаётся только по id.</summary>
    public int Area { get; init; }
    /// <summary>Номер блока карты (mAA_BB → BB).</summary>
    public int Block { get; init; }
    public bool IsPrimaryForMap { get; init; }
    public List<string> TipsRu { get; init; } = new();
}

public sealed record BossInfo
{
    public required string Id { get; init; }
    public required string NameRu { get; init; }
    public required string NameEn { get; init; }
    public required string LocationId { get; init; }
    public bool Optional { get; init; }
    public string TipRu { get; init; } = "";
}

public sealed record UpgradeStep
{
    /// <summary>Уровень, НА который улучшаем (например 6 = «+5 → +6»).</summary>
    public int ToLevel { get; init; }
    public required string MaterialItemId { get; init; }
    public int Quantity { get; init; }
    /// <summary>Уголь, который должен быть отдан кузнецу (null — не нужен).</summary>
    public string? EmberItemId { get; init; }
    public int Souls { get; init; }
}

public sealed record UpgradePath
{
    public required string Id { get; init; }
    public required string NameRu { get; init; }
    public required string SmithRu { get; init; }
    public List<UpgradeStep> Steps { get; init; } = new();
    public int MaxLevel => Steps.Count == 0 ? 0 : Steps.Max(s => s.ToLevel);
}

public sealed record WeaponUpgradeInfo
{
    /// <summary>Совпадает с id предмета в items.json.</summary>
    public required string ItemId { get; init; }
    public int GameId { get; init; }
    public required string PathId { get; init; }
    public string ScalingStr { get; init; } = "-";
    public string ScalingDex { get; init; } = "-";
    public string ScalingInt { get; init; } = "-";
    public string ScalingFth { get; init; } = "-";
    public string ScalingHintRu { get; init; } = "";
}

public sealed record WeaponUpgradeData
{
    public List<UpgradePath> Paths { get; init; } = new();
    public List<WeaponUpgradeInfo> Weapons { get; init; } = new();
}
