using DS1Companion.Core.Logic;
using DS1Companion.Core.Models;

namespace DS1Companion.Tests;

public class InventoryDiffTests
{
    private static InventoryEntry G(int id, int qty) => new(ItemKind.Goods, id, qty);

    [Fact]
    public void NewItem_IsReported()
    {
        var gains = InventoryDiff.Compute(new[] { G(200, 5) }, new[] { G(200, 5), G(1000, 1) });
        Assert.Equal(new[] { new InventoryGain(ItemKind.Goods, 1000, 1) }, gains);
    }

    [Fact]
    public void QuantityIncrease_ReportsOnlyDelta()
    {
        var gains = InventoryDiff.Compute(new[] { G(1000, 3) }, new[] { G(1000, 7) });
        Assert.Single(gains);
        Assert.Equal(4, gains[0].Quantity);
    }

    [Fact]
    public void ConsumedOrDropped_IsIgnored()
    {
        Assert.Empty(InventoryDiff.Compute(new[] { G(200, 5), G(1000, 2) }, new[] { G(200, 4) }));
    }

    [Fact]
    public void SameIdDifferentKind_AreDistinct()
    {
        var gains = InventoryDiff.Compute(new[] { G(100, 1) }, new[] { G(100, 1), new InventoryEntry(ItemKind.Ring, 100, 1) });
        Assert.Equal(ItemKind.Ring, Assert.Single(gains).Kind);
    }

    [Fact]
    public void DuplicateSlots_AreAggregated_AndEmptySlotsSkipped()
    {
        var before = new[] { new InventoryEntry(ItemKind.Weapon, 301000, 1) };
        var after = new[] { new InventoryEntry(ItemKind.Weapon, 301000, 1), new InventoryEntry(ItemKind.Weapon, 301000, 1), G(-1, 0) };
        var gain = Assert.Single(InventoryDiff.Compute(before, after));
        Assert.Equal(1, gain.Quantity);
    }

    [Fact]
    public void MissingBaseline_ProducesNothing()
    {
        Assert.Empty(InventoryDiff.Compute(null, new[] { G(1, 1) }));
        Assert.Empty(InventoryDiff.Compute(new[] { G(1, 1) }, null));
    }
}

public class PopupCountdownTests
{
    [Fact]
    public void Expires_AfterTenSeconds()
    {
        var c = new PopupCountdown();
        var fired = 0;
        c.Expired += (_, _) => fired++;

        for (int i = 0; i < 99; i++) Assert.False(c.Advance(TimeSpan.FromMilliseconds(100)));
        Assert.False(c.IsExpired);
        Assert.True(c.Advance(TimeSpan.FromMilliseconds(100)));
        Assert.True(c.IsExpired);
        Assert.Equal(1, fired);
        Assert.False(c.Advance(TimeSpan.FromSeconds(5))); // повторно не срабатывает
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Hover_PausesAndResumes()
    {
        var c = new PopupCountdown(TimeSpan.FromSeconds(10));
        c.Advance(TimeSpan.FromSeconds(4));
        c.Pause();
        c.Advance(TimeSpan.FromMinutes(5)); // мышь держат над карточкой сколько угодно
        Assert.False(c.IsExpired);
        Assert.Equal(TimeSpan.FromSeconds(6), c.Remaining);

        c.Resume();
        c.Advance(TimeSpan.FromSeconds(5.9));
        Assert.False(c.IsExpired);
        c.Advance(TimeSpan.FromSeconds(0.2));
        Assert.True(c.IsExpired);
    }

    [Fact]
    public void RemainingFraction_GoesFromOneToZero()
    {
        var c = new PopupCountdown(TimeSpan.FromSeconds(10));
        Assert.Equal(1.0, c.RemainingFraction, 3);
        c.Advance(TimeSpan.FromSeconds(2.5));
        Assert.Equal(0.75, c.RemainingFraction, 3);
        c.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0.0, c.RemainingFraction, 3);
    }

    [Fact]
    public void Complete_ClosesImmediately_Once()
    {
        var c = new PopupCountdown();
        var fired = 0;
        c.Expired += (_, _) => fired++;
        c.Complete();
        c.Complete();
        Assert.True(c.IsExpired);
        Assert.Equal(1, fired);
    }

    [Fact]
    public void NegativeOrZeroDelta_IsIgnored()
    {
        var c = new PopupCountdown();
        c.Advance(TimeSpan.Zero);
        c.Advance(TimeSpan.FromSeconds(-3));
        Assert.Equal(TimeSpan.Zero, c.Elapsed);
    }
}

public class SoulsCalculatorTests
{
    [Theory]
    [InlineData(2, 673)]
    [InlineData(5, 724)]
    [InlineData(11, 829)]
    [InlineData(12, 847)]
    [InlineData(13, 1039)]
    [InlineData(100, 60265)]
    public void CostToReach_MatchesKnownValues(int level, int expected) =>
        Assert.Equal(expected, SoulsCalculator.CostToReach(level));

    [Fact]
    public void CostIsMonotonic()
    {
        for (int l = 2; l < 300; l++)
            Assert.True(SoulsCalculator.CostToReach(l + 1) >= SoulsCalculator.CostToReach(l), $"уровень {l}");
    }

    [Fact]
    public void Edges_AreSafe()
    {
        Assert.Equal(0, SoulsCalculator.CostToReach(1));
        Assert.Equal(0, SoulsCalculator.CostToReach(SoulsCalculator.MaxLevel + 1));
        Assert.Equal(1.0, SoulsCalculator.Progress(100, SoulsCalculator.MaxLevel));
    }

    [Fact]
    public void Progress_IsClamped()
    {
        var cost = SoulsCalculator.CostForNextLevel(24);
        Assert.Equal(0.5, SoulsCalculator.Progress(cost / 2, 24), 2);
        Assert.Equal(1.0, SoulsCalculator.Progress(cost * 3, 24));
        Assert.Equal(0.0, SoulsCalculator.Progress(-50, 24));
    }

    [Fact]
    public void AffordableLevels_CountsChainedPurchases()
    {
        var two = SoulsCalculator.CostToReach(25) + SoulsCalculator.CostToReach(26);
        Assert.Equal(2, SoulsCalculator.AffordableLevels(two, 24));
        Assert.Equal(1, SoulsCalculator.AffordableLevels(two - 1, 24));
        Assert.Equal(0, SoulsCalculator.AffordableLevels(0, 24));
    }
}

public class RarityFilterTests
{
    [Fact]
    public void AtLeast_FiltersByOrder()
    {
        var items = Enum.GetValues<Rarity>().Select(r => TestData.Item(r.ToString(), r)).ToList();
        var result = RarityFilter.AtLeast(items, Rarity.Rare).Select(i => i.Rarity).ToList();
        Assert.Equal(new[] { Rarity.Rare, Rarity.VeryRare, Rarity.Legendary }, result);
        Assert.Equal(5, RarityFilter.AtLeast(items, Rarity.Common).Count());
    }

    [Fact]
    public void KeyItems_AlwaysPopup()
    {
        var key = TestData.Item("k", Rarity.Common, ItemCategory.KeyItem);
        var junk = TestData.Item("j", Rarity.Common, ItemCategory.Junk);
        Assert.True(RarityFilter.ShouldShowPopup(key, Rarity.Legendary));
        Assert.False(RarityFilter.ShouldShowPopup(junk, Rarity.Uncommon));
        Assert.True(RarityFilter.ShouldShowPopup(junk, Rarity.Common));
    }

    [Theory]
    [InlineData(Rarity.Common, null)]
    [InlineData(Rarity.Rare, null)]
    [InlineData(Rarity.VeryRare, SoundCue.VeryRareChime)]
    [InlineData(Rarity.Legendary, SoundCue.LegendaryChord)]
    public void SoundCue_PerRarity(Rarity r, SoundCue? expected) => Assert.Equal(expected, RarityFilter.CueFor(r));

    [Fact]
    public void Collectible_IsRareAndAbove()
    {
        Assert.False(RarityFilter.IsCollectible(TestData.Item("a", Rarity.Uncommon)));
        Assert.True(RarityFilter.IsCollectible(TestData.Item("b", Rarity.Rare)));
    }

    [Fact]
    public void RarityColors_MatchSpec()
    {
        Assert.Equal("#9CA3AF", DisplayText.RarityColorHex(Rarity.Common));
        Assert.Equal("#22C55E", DisplayText.RarityColorHex(Rarity.Uncommon));
        Assert.Equal("#3B82F6", DisplayText.RarityColorHex(Rarity.Rare));
        Assert.Equal("#A855F7", DisplayText.RarityColorHex(Rarity.VeryRare));
        Assert.Equal("#F59E0B", DisplayText.RarityColorHex(Rarity.Legendary));
        Assert.Equal("ЛЕГЕНДАРНЫЙ", DisplayText.RarityLabelRu(Rarity.Legendary));
    }
}

public class SoftCapAndUpgradeTests
{
    [Fact]
    public void Resistance_AboveBase_IsWarned()
    {
        var hints = SoftCapAdvisor.Analyze(new CharacterStats { Vitality = 40, Resistance = 20, Attunement = 19 });
        Assert.Equal(StatAdvice.Warning, hints.Single(h => h.Key == "resistance").Advice);
        Assert.Equal(StatAdvice.Efficient, hints.Single(h => h.Key == "vitality").Advice);
        Assert.Contains("5", hints.Single(h => h.Key == "attunement").HintRu); // 19 ученость = 5 слотов
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(19, 5)]
    [InlineData(50, 10)]
    public void AttunementSlots(int att, int slots) => Assert.Equal(slots, SoftCapAdvisor.AttunementSlotsAt(att));

    [Fact]
    public void Claymore5_NextStep_NeedsLargeShardAndEmber()
    {
        var kb = TestData.Knowledge;
        var claymore = kb.FindItem("claymore")!.GameId!.Value;
        var advice = new UpgradeAdvisor(kb).Advise(new EquippedWeapon { RawId = claymore + 5, Durability = 100, MaxDurability = 100 })!;
        Assert.Equal("Клеймор +5", advice.DisplayName);
        Assert.Contains("Силы и Ловкости", advice.ScalingHint);
        Assert.Contains("Для +6", advice.NextStep);
        Assert.Contains("Большой осколок титанита", advice.NextStep);
        Assert.Contains("Большой уголь", advice.NextStep);
        Assert.Contains("Андрэ из Асторы", advice.NextStep);
    }

    [Fact]
    public void MaxedAndInfusedWeapons()
    {
        var kb = TestData.Knowledge;
        var advisor = new UpgradeAdvisor(kb);
        var claymore = kb.FindItem("claymore")!.GameId!.Value;
        Assert.True(advisor.Advise(new EquippedWeapon { RawId = claymore + 15 })!.IsMaxed);

        var drake = kb.FindItem("drake_sword")!.GameId!.Value;
        Assert.Contains("Чешуя дракона", advisor.Advise(new EquippedWeapon { RawId = drake + 2 })!.NextStep);

        var fireClaymore = WeaponIdCodec.Compose(claymore, 8, 3);
        Assert.StartsWith("Огненный клеймор +3", advisor.Advise(new EquippedWeapon { RawId = fireClaymore })!.DisplayName);
        Assert.Null(advisor.Advise(null));
    }

    [Fact]
    public void WeaponIdCodec_RoundTrips()
    {
        var raw = WeaponIdCodec.Compose(301000, 4, 7);
        Assert.Equal(301407, raw);
        Assert.Equal(301000, WeaponIdCodec.BaseId(raw));
        Assert.Equal(4, WeaponIdCodec.Infusion(raw));
        Assert.Equal(7, WeaponIdCodec.UpgradeLevel(raw));
    }
}
