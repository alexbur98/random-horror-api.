using System.Net;
using DS1Companion.Core.Models;
using DS1Companion.Core.Services;

namespace DS1Companion.Tests;

public class IconResolverTests
{
    [Fact]
    public void IconKey_TakesPriority_OverId()
    {
        var files = new HashSet<string> { Path.Combine("icons", "estus.png"), Path.Combine("icons", "estus_flask.png") };
        var resolver = new IconResolver(new[] { "icons" }, files.Contains);
        var src = resolver.Resolve(TestData.Item("estus_flask", iconKey: "estus"), out var download);
        Assert.Equal(Path.Combine("icons", "estus.png"), src.FilePath);
        Assert.False(download);
    }

    [Fact]
    public void FallsBack_ToId_ThenToSecondDirectory()
    {
        var files = new HashSet<string> { Path.Combine("cache", "titanite_shard.png") };
        var resolver = new IconResolver(new[] { "icons", "cache" }, files.Contains);
        var src = resolver.Resolve(TestData.Item("titanite_shard", iconKey: "missing"), out _);
        Assert.Equal(Path.Combine("cache", "titanite_shard.png"), src.FilePath);
    }

    [Theory]
    [InlineData(ItemCategory.KeyItem, IconGlyph.Key)]
    [InlineData(ItemCategory.Upgrade, IconGlyph.Titanite)]
    [InlineData(ItemCategory.Consumable, IconGlyph.Flask)]
    [InlineData(ItemCategory.Spell, IconGlyph.Scroll)]
    [InlineData(ItemCategory.Junk, IconGlyph.Soul)]
    public void NoFile_UsesCategoryGlyph(ItemCategory category, IconGlyph glyph)
    {
        var resolver = new IconResolver(new[] { "icons" }, _ => false);
        var src = resolver.Resolve(TestData.Item("x", category: category), out var download);
        Assert.False(src.HasImage);
        Assert.Equal(glyph, src.Glyph);
        Assert.False(download);
    }

    [Fact]
    public void EquipmentGlyph_DependsOnKind()
    {
        var ring = new ItemInfo { Id = "r", NameRu = "r", NameEn = "r", Category = ItemCategory.Equipment, GameKind = ItemKind.Ring };
        var weapon = ring with { GameKind = ItemKind.Weapon };
        var shield = weapon with { Glyph = IconGlyph.Shield };
        Assert.Equal(IconGlyph.Ring, ring.EffectiveGlyph);
        Assert.Equal(IconGlyph.Sword, weapon.EffectiveGlyph);
        Assert.Equal(IconGlyph.Shield, shield.EffectiveGlyph);
    }

    [Theory]
    [InlineData("https://example.com/a.png", true)]
    [InlineData("ftp://example.com/a.png", false)]
    [InlineData("not a url", false)]
    public void Download_RequestedOnlyForHttpUrls(string url, bool expected)
    {
        var resolver = new IconResolver(new[] { "icons" }, _ => false);
        resolver.Resolve(TestData.Item("x", iconUrl: url), out var download);
        Assert.Equal(expected, download);
    }

    [Fact]
    public void Sanitize_StripsPathTraversal()
    {
        var name = IconResolver.Sanitize("../../evil/name");
        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("\\", name);
        Assert.False(name.StartsWith("."));
    }

    [Fact]
    public void AllKnowledgeBaseItems_HaveGlyphResources()
    {
        // Каждому глифу соответствует ресурс Glyph.{name} в Themes/Glyphs.xaml.
        var xaml = File.ReadAllText(Path.Combine(TestData.SolutionDir, "src", "DS1Companion.UI", "Themes", "Glyphs.xaml"));
        foreach (var glyph in Enum.GetValues<IconGlyph>())
            Assert.Contains($"x:Key=\"Glyph.{glyph}\"", xaml);
    }
}

public class CachingIconProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int Calls;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(_respond(request));
        }
    }

    private static readonly byte[] TinyPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    [Fact]
    public async Task DownloadsInBackground_CachesFile_AndRaisesEvent()
    {
        using var dir = new TempDir();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TinyPng) });
        using var provider = new CachingIconProvider(new[] { dir.Path }, dir.Path, new HttpClient(handler));
        var updated = new List<string>();
        provider.IconUpdated += updated.Add;
        var item = TestData.Item("drake_sword", iconUrl: "https://icons.example/drake.png");

        var first = provider.Resolve(item); // не блокирует — сразу векторная иконка
        Assert.False(first.HasImage);
        provider.Resolve(item);              // повторный запрос не создаёт второе скачивание
        await provider.WhenIdle();

        Assert.Equal(1, handler.Calls);
        Assert.Equal(new[] { "drake_sword" }, updated);
        var second = provider.Resolve(item);
        Assert.True(second.HasImage);
        Assert.Equal(Path.Combine(dir.Path, "drake_sword.png"), second.FilePath);
    }

    [Fact]
    public async Task NonPngOrHttpError_KeepsVectorFallback()
    {
        using var dir = new TempDir();
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.Contains("html")
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>nope</html>") }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var provider = new CachingIconProvider(new[] { dir.Path }, dir.Path, new HttpClient(handler));
        provider.Resolve(TestData.Item("a", iconUrl: "https://x.example/page.html"));
        provider.Resolve(TestData.Item("b", iconUrl: "https://x.example/missing.png"));
        await provider.WhenIdle();
        Assert.Empty(Directory.GetFiles(dir.Path));
    }
}
