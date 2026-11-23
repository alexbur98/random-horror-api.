using System.Collections.Concurrent;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Services;

/// <summary>Результат подбора иконки: локальный PNG либо векторный fallback.</summary>
public sealed record IconSource(string? FilePath, IconGlyph Glyph)
{
    public bool HasImage => FilePath is not null;
    public static IconSource ForGlyph(IconGlyph glyph) => new(null, glyph);
}

public interface IIconProvider
{
    IconSource Resolve(ItemInfo item);
    /// <summary>Иконка скачалась в кэш — UI должен перезапросить её для этого item.Id.</summary>
    event Action<string>? IconUpdated;
}

/// <summary>
/// Чистая логика выбора иконки (без WPF): {icon_key}.png → {id}.png во всех каталогах поиска;
/// иначе — векторный глиф категории; если есть icon_url, просим скачать.
/// </summary>
public sealed class IconResolver
{
    private readonly IReadOnlyList<string> _searchDirs;
    private readonly Func<string, bool> _fileExists;

    public IconResolver(IReadOnlyList<string> searchDirs, Func<string, bool>? fileExists = null)
    {
        _searchDirs = searchDirs;
        _fileExists = fileExists ?? File.Exists;
    }

    public static IEnumerable<string> CandidateFileNames(ItemInfo item)
    {
        if (!string.IsNullOrWhiteSpace(item.IconKey)) yield return Sanitize(item.IconKey!) + ".png";
        yield return Sanitize(item.Id) + ".png";
    }

    public IconSource Resolve(ItemInfo item, out bool needsDownload)
    {
        foreach (var dir in _searchDirs)
            foreach (var name in CandidateFileNames(item))
            {
                var path = Path.Combine(dir, name);
                if (_fileExists(path))
                {
                    needsDownload = false;
                    return new IconSource(path, item.EffectiveGlyph);
                }
            }

        needsDownload = !string.IsNullOrWhiteSpace(item.IconUrl)
                        && Uri.TryCreate(item.IconUrl, UriKind.Absolute, out var uri)
                        && uri.Scheme is "https" or "http";
        return IconSource.ForGlyph(item.EffectiveGlyph);
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
        return new string(chars).Trim().Trim('.');
    }
}

/// <summary>
/// Провайдер иконок с фоновым кэшированием: если файла нет, но есть icon_url,
/// аккуратно скачивает PNG (один раз на предмет, не блокируя вызывающий поток).
/// </summary>
public sealed class CachingIconProvider : IIconProvider, IDisposable
{
    private const long MaxIconBytes = 2 * 1024 * 1024;
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47 };

    private readonly IconResolver _resolver;
    private readonly string _cacheDir;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly ConcurrentDictionary<string, Task> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<Action> _post;

    public event Action<string>? IconUpdated;

    /// <param name="searchDirs">Каталоги поиска (первым — локальный Assets/Icons).</param>
    /// <param name="cacheDir">Куда складывать скачанные иконки (должен быть в searchDirs).</param>
    /// <param name="post">Маршалинг события в UI-поток.</param>
    public CachingIconProvider(IReadOnlyList<string> searchDirs, string cacheDir, HttpClient? http = null, Action<Action>? post = null)
    {
        _resolver = new IconResolver(searchDirs);
        _cacheDir = cacheDir;
        _ownsHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _post = post ?? (a => a());
    }

    public IconSource Resolve(ItemInfo item)
    {
        var source = _resolver.Resolve(item, out var needsDownload);
        if (needsDownload) _downloads.GetOrAdd(item.Id, _ => Task.Run(() => DownloadAsync(item)));
        return source;
    }

    /// <summary>Для тестов: дождаться всех фоновых скачиваний.</summary>
    public Task WhenIdle() => Task.WhenAll(_downloads.Values);

    private async Task DownloadAsync(ItemInfo item)
    {
        try
        {
            using var response = await _http.GetAsync(item.IconUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            if (response.Content.Headers.ContentLength is > MaxIconBytes) return;

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length is 0 or > (int)MaxIconBytes || !bytes.AsSpan().StartsWith(PngMagic)) return;

            Directory.CreateDirectory(_cacheDir);
            var name = IconResolver.CandidateFileNames(item).First();
            var target = Path.Combine(_cacheDir, name);
            var temp = target + ".part";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, target, overwrite: true);
            _post(() => IconUpdated?.Invoke(item.Id));
        }
        catch (Exception)
        {
            // Нет сети или сайт недоступен — остаёмся на векторной иконке, UI не страдает.
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
