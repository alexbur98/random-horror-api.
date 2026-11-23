using System.Text.Json;
using System.Text.Json.Serialization;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Data;

/// <summary>Вся статическая база знаний: предметы, оружие, локации, боссы.</summary>
public sealed class KnowledgeBase
{
    public static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly Dictionary<string, ItemInfo> _itemsById;
    private readonly Dictionary<(ItemKind, int), ItemInfo> _itemsByGameId;
    private readonly Dictionary<string, LocationInfo> _locationsById;
    private readonly Dictionary<string, BossInfo> _bossesById;
    private readonly Dictionary<string, UpgradePath> _pathsById;
    private readonly Dictionary<string, WeaponUpgradeInfo> _weaponsByItemId;

    public IReadOnlyList<ItemInfo> Items { get; }
    public IReadOnlyList<LocationInfo> Locations { get; }
    public IReadOnlyList<BossInfo> Bosses { get; }
    public WeaponUpgradeData Upgrades { get; }

    public KnowledgeBase(IEnumerable<ItemInfo> items, WeaponUpgradeData upgrades,
        IEnumerable<LocationInfo> locations, IEnumerable<BossInfo> bosses)
    {
        Items = items.ToList();
        Upgrades = upgrades;
        Locations = locations.ToList();
        Bosses = bosses.ToList();

        _itemsById = new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);
        _itemsByGameId = new Dictionary<(ItemKind, int), ItemInfo>();
        foreach (var item in Items)
        {
            if (!_itemsById.TryAdd(item.Id, item))
                throw new InvalidDataException($"Дублирующийся id предмета: {item.Id}");
            if (item.GameKind is { } kind && item.GameId is { } gid)
                _itemsByGameId.TryAdd((kind, gid), item);
        }

        _locationsById = Locations.ToDictionary(l => l.Id, StringComparer.OrdinalIgnoreCase);
        _bossesById = Bosses.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);
        _pathsById = upgrades.Paths.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        _weaponsByItemId = upgrades.Weapons.ToDictionary(w => w.ItemId, StringComparer.OrdinalIgnoreCase);
    }

    public static KnowledgeBase LoadFromDirectory(string dataDir)
    {
        T Read<T>(string file)
        {
            var path = Path.Combine(dataDir, file);
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, JsonOptions)
                   ?? throw new InvalidDataException($"Пустой файл данных: {path}");
        }

        return new KnowledgeBase(
            Read<List<ItemInfo>>("items_ds1.json"),
            Read<WeaponUpgradeData>("weapons_upgrade_ds1.json"),
            Read<List<LocationInfo>>("locations_ds1.json"),
            Read<List<BossInfo>>("bosses_ds1.json"));
    }

    public ItemInfo? FindItem(string id) => _itemsById.GetValueOrDefault(id);

    public ItemInfo ResolveInventoryItem(ItemKind kind, int gameId)
    {
        var lookupId = kind == ItemKind.Weapon ? WeaponIdCodec.BaseId(gameId) : gameId;
        return _itemsByGameId.GetValueOrDefault((kind, lookupId)) ?? ItemInfo.Unknown(kind, gameId);
    }

    public ItemInfo? FindWeaponItem(int rawWeaponId) =>
        _itemsByGameId.GetValueOrDefault((ItemKind.Weapon, WeaponIdCodec.BaseId(rawWeaponId)));

    public LocationInfo? FindLocation(string id) => _locationsById.GetValueOrDefault(id);

    public LocationInfo? FindLocation(int area, int block) =>
        Locations.Where(l => l.Area == area && l.Block == block)
                 .OrderByDescending(l => l.IsPrimaryForMap)
                 .FirstOrDefault();

    public LocationInfo? ResolveLocation(GameSnapshot s)
    {
        if (s.Area is { } a && s.Block is { } b && FindLocation(a, b) is { } byMap) return byMap;
        return s.LocationId is { } id ? FindLocation(id) : null;
    }

    public BossInfo? FindBoss(string id) => _bossesById.GetValueOrDefault(id);

    public IEnumerable<BossInfo> BossesIn(string locationId) =>
        Bosses.Where(b => string.Equals(b.LocationId, locationId, StringComparison.OrdinalIgnoreCase));

    public UpgradePath? FindPath(string id) => _pathsById.GetValueOrDefault(id);

    public WeaponUpgradeInfo? FindWeaponUpgrade(string itemId) => _weaponsByItemId.GetValueOrDefault(itemId);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
