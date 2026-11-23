using DS1Companion.Core.Data;
using DS1Companion.Core.Memory;
using DS1Companion.Core.Models;

namespace DS1Companion.Tests;

public class KnowledgeBaseDataTests
{
    private static KnowledgeBase Kb => TestData.Knowledge;

    [Fact]
    public void Items_AreEnough_AndCoverAllRarities()
    {
        Assert.InRange(Kb.Items.Count, 80, 200);
        foreach (var r in Enum.GetValues<Rarity>()) Assert.Contains(Kb.Items, i => i.Rarity == r);
        foreach (var c in Enum.GetValues<ItemCategory>()) Assert.Contains(Kb.Items, i => i.Category == c);
    }

    [Fact]
    public void Items_HaveRequiredFields()
    {
        foreach (var i in Kb.Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(i.NameRu), i.Id);
            Assert.False(string.IsNullOrWhiteSpace(i.NameEn), i.Id);
            Assert.False(string.IsNullOrWhiteSpace(i.SummaryRu), i.Id);
            Assert.False(string.IsNullOrWhiteSpace(i.RarityReason), i.Id);
            Assert.False(string.IsNullOrWhiteSpace(i.IconKey), i.Id);
            Assert.Matches("^[a-z0-9_]+$", i.Id);
            Assert.DoesNotContain("?", i.SummaryRu + i.RarityReason);
        }
    }

    [Fact]
    public void GameIds_AreUniquePerKind()
    {
        var dupes = Kb.Items.Where(i => i.GameId is not null)
            .GroupBy(i => (i.GameKind, i.GameId)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void BalderSideSword_IsLegendaryWeapon()
    {
        var balder = Kb.FindItem("balder_side_sword")!;
        Assert.Equal(Rarity.Legendary, balder.Rarity);
        Assert.Equal("Балдерский прямой меч", balder.NameRu);
        Assert.Same(balder, Kb.ResolveInventoryItem(ItemKind.Weapon, WeaponIdCodec.Compose(balder.GameId!.Value, 0, 3)));
    }

    [Fact]
    public void UnknownInventoryItem_GetsPlaceholder()
    {
        var unknown = Kb.ResolveInventoryItem(ItemKind.Goods, 987654);
        Assert.StartsWith("unknown_", unknown.Id);
        Assert.Equal(IconGlyph.Unknown, unknown.EffectiveGlyph);
    }

    [Fact]
    public void Weapons_ReferenceExistingItemsAndPaths()
    {
        Assert.NotEmpty(Kb.Upgrades.Weapons);
        foreach (var w in Kb.Upgrades.Weapons)
        {
            var item = Kb.FindItem(w.ItemId);
            Assert.NotNull(item);
            if (item!.GameId is { } gid) Assert.Equal(gid, w.GameId);
            Assert.NotNull(Kb.FindPath(w.PathId));
        }
        foreach (var path in Kb.Upgrades.Paths)
        foreach (var step in path.Steps)
        {
            Assert.NotNull(Kb.FindItem(step.MaterialItemId));
            if (step.EmberItemId is { } ember) Assert.NotNull(Kb.FindItem(ember));
        }
        Assert.Equal(15, Kb.FindPath("regular")!.MaxLevel);
    }

    [Fact]
    public void Locations_AndBosses_AreConsistent()
    {
        Assert.True(Kb.Locations.Count >= 20);
        Assert.NotNull(Kb.FindLocation("undead_burg"));
        Assert.Equal("Город Нежити", Kb.FindLocation(10, 1)!.NameRu); // primary для общей карты m10_01
        foreach (var l in Kb.Locations) Assert.NotEmpty(l.TipsRu);
        foreach (var b in Kb.Bosses) Assert.NotNull(Kb.FindLocation(b.LocationId));
        foreach (var group in Kb.Locations.GroupBy(l => (l.Area, l.Block)))
            Assert.Equal(1, group.Count(l => l.IsPrimaryForMap));
    }

    [Fact]
    public void OffsetsJson_IsValid()
    {
        var cfg = OffsetsConfig.Load(Path.Combine(TestData.DataDir, "offsets.json"));
        Assert.Equal("DarkSoulsRemastered", cfg.ProcessName);
        Assert.Empty(cfg.Validate());
        foreach (var field in new[] { "deaths", "souls", "level", "character_name", "weapon_id", "area" })
            Assert.True(cfg.Fields.ContainsKey(field), field);
        Assert.NotNull(cfg.Inventory);
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var a = TestData.Item("same");
        Assert.Throws<InvalidDataException>(() => new KnowledgeBase(new[] { a, a }, new WeaponUpgradeData(),
            Array.Empty<LocationInfo>(), Array.Empty<BossInfo>()));
    }
}
