using System.Text.Json;
using DS1Companion.Core.Data;
using DS1Companion.Core.Models;
using DS1Companion.Core.Services;

namespace DS1Companion.Core.Persistence;

/// <summary>Пути хранения данных пользователя: %AppData%/DS1Companion/.</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string ProfilesDir => Path.Combine(Root, "profiles");
    public string IconCacheDir => Path.Combine(Root, "IconCache");
    public string LogsDir => Path.Combine(Root, "logs");

    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DS1Companion");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(LogsDir);
    }
}

public static class JsonFile
{
    public static T LoadOrDefault<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            using var s = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(s, KnowledgeBase.JsonOptions) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Повреждённый файл не должен ронять приложение — сохраняем копию и начинаем с чистого листа.
            TryBackup(path);
            return fallback();
        }
    }

    /// <summary>Атомарная запись: сначала во временный файл, затем замена.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, KnowledgeBase.JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryBackup(string path)
    {
        try { if (File.Exists(path)) File.Copy(path, path + ".broken", overwrite: true); } catch { /* ignore */ }
    }
}

public sealed class AppSettings
{
    public double Volume { get; set; } = 0.6;
    public bool IsMuted { get; set; }
    public bool IsTopmost { get; set; }
    public bool StartInDemo { get; set; }
    public Rarity PopupMinRarity { get; set; } = Rarity.Common;
    public double PopupSeconds { get; set; } = 10;

    public string? MonitorDeviceName { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }
}

public sealed class SettingsStore
{
    private readonly AppPaths _paths;
    public SettingsStore(AppPaths paths) => _paths = paths;
    public AppSettings Load() => JsonFile.LoadOrDefault(_paths.SettingsFile, () => new AppSettings());
    public void Save(AppSettings settings) => JsonFile.Save(_paths.SettingsFile, settings);
}

public sealed class BossRecord
{
    public string BossId { get; set; } = "";
    public int Attempts { get; set; }
    public int Defeats { get; set; }
    public bool Defeated { get; set; }
    public double TotalSeconds { get; set; }
    public double? BestVictorySeconds { get; set; }
    public DateTime? DefeatedAt { get; set; }
}

public sealed class CollectedItem
{
    public string ItemId { get; set; } = "";
    public DateTime FirstFoundAt { get; set; }
    public int TimesFound { get; set; }
}

public sealed class SessionRecord
{
    public DateTime StartedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int Deaths { get; set; }
    public int ItemsPicked { get; set; }
}

public sealed class CharacterProfile
{
    public string CharacterName { get; set; } = "";
    public int LastKnownDeaths { get; set; }
    public double TotalPlaySeconds { get; set; }
    public Dictionary<string, BossRecord> Bosses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CollectedItem> RareCollection { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SessionRecord> Sessions { get; set; } = new();

    public BossRecord Boss(string bossId)
    {
        if (!Bosses.TryGetValue(bossId, out var r))
            Bosses[bossId] = r = new BossRecord { BossId = bossId };
        return r;
    }

    public bool AddToCollection(string itemId, DateTime now)
    {
        if (RareCollection.TryGetValue(itemId, out var existing))
        {
            existing.TimesFound++;
            return false;
        }
        RareCollection[itemId] = new CollectedItem { ItemId = itemId, FirstFoundAt = now, TimesFound = 1 };
        return true;
    }
}

/// <summary>Раздельные профили по имени персонажа: profiles/{имя}.json.</summary>
public sealed class ProfileStore
{
    private readonly AppPaths _paths;
    public ProfileStore(AppPaths paths) => _paths = paths;

    public string FileFor(string characterName)
    {
        var safe = IconResolver.Sanitize(string.IsNullOrWhiteSpace(characterName) ? "unnamed" : characterName);
        return Path.Combine(_paths.ProfilesDir, (safe.Length == 0 ? "unnamed" : safe) + ".json");
    }

    public CharacterProfile Load(string characterName)
    {
        var p = JsonFile.LoadOrDefault(FileFor(characterName), () => new CharacterProfile());
        p.CharacterName = characterName;
        // System.Text.Json создаёт словари без нашего компаратора — восстанавливаем его.
        p.Bosses = new Dictionary<string, BossRecord>(p.Bosses, StringComparer.OrdinalIgnoreCase);
        p.RareCollection = new Dictionary<string, CollectedItem>(p.RareCollection, StringComparer.OrdinalIgnoreCase);
        return p;
    }

    public void Save(CharacterProfile profile) => JsonFile.Save(FileFor(profile.CharacterName), profile);
}
