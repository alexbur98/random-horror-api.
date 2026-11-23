using DS1Companion.Core.Game;
using DS1Companion.Core.Logic;
using DS1Companion.Core.Models;
using DS1Companion.Core.Persistence;
using DS1Companion.Core.Services;

namespace DS1Companion.Tests;

public class PersistenceTests
{
    [Fact]
    public void Settings_RoundTrip()
    {
        using var dir = new TempDir();
        var store = new SettingsStore(new AppPaths(dir.Path));
        store.Save(new AppSettings { Volume = 0.3, IsTopmost = true, PopupMinRarity = Rarity.Rare, WindowLeft = 1920, MonitorDeviceName = @"\\.\DISPLAY2" });
        var loaded = store.Load();
        Assert.Equal(0.3, loaded.Volume);
        Assert.True(loaded.IsTopmost);
        Assert.Equal(Rarity.Rare, loaded.PopupMinRarity);
        Assert.Equal(1920, loaded.WindowLeft);
        Assert.Equal(@"\\.\DISPLAY2", loaded.MonitorDeviceName);
        Assert.Contains("\"popup_min_rarity\": \"Rare\"", File.ReadAllText(Path.Combine(dir.Path, "settings.json")));
    }

    [Fact]
    public void CorruptedSettings_FallBackToDefaults_AndKeepBackup()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Path);
        File.WriteAllText(paths.SettingsFile, "{ not json");
        var loaded = new SettingsStore(paths).Load();
        Assert.Equal(new AppSettings().Volume, loaded.Volume);
        Assert.True(File.Exists(paths.SettingsFile + ".broken"));
    }

    [Fact]
    public void Profiles_AreSeparatePerCharacter()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Path);
        paths.EnsureCreated();
        var store = new ProfileStore(paths);

        var a = store.Load("Солер");
        a.AddToCollection("balder_side_sword", DateTime.Now);
        BossStatsRecorder.RecordStart(a, "taurus_demon");
        store.Save(a);
        store.Save(store.Load("Оскар"));

        var reloaded = store.Load("Солер");
        Assert.True(reloaded.RareCollection.ContainsKey("BALDER_SIDE_SWORD")); // регистронезависимо
        Assert.Equal(1, reloaded.Boss("taurus_demon").Attempts);
        Assert.Empty(store.Load("Оскар").RareCollection);
        Assert.Equal(2, Directory.GetFiles(paths.ProfilesDir).Length);
        Assert.False(a.AddToCollection("balder_side_sword", DateTime.Now)); // повторная находка
    }

    [Fact]
    public void UnsafeCharacterNames_StayInsideProfilesDir()
    {
        using var dir = new TempDir();
        var paths = new AppPaths(dir.Path);
        var file = new ProfileStore(paths).FileFor("../../../evil");
        Assert.StartsWith(Path.GetFullPath(paths.ProfilesDir), Path.GetFullPath(file));
    }

    [Fact]
    public void BossRecorder_TracksBestVictory()
    {
        var p = new CharacterProfile();
        var boss = TestData.Knowledge.FindBoss("asylum_demon")!;
        BossStatsRecorder.RecordEnd(p, new BossFightResult(boss, true, TimeSpan.FromSeconds(90)), DateTime.Now);
        BossStatsRecorder.RecordEnd(p, new BossFightResult(boss, true, TimeSpan.FromSeconds(60)), DateTime.Now);
        BossStatsRecorder.RecordEnd(p, new BossFightResult(boss, false, TimeSpan.FromSeconds(10)), DateTime.Now);
        var r = p.Boss(boss.Id);
        Assert.Equal(60, r.BestVictorySeconds);
        Assert.Equal(160, r.TotalSeconds);
        Assert.Equal(1, r.Defeats);
    }
}

public class WavSynthTests
{
    [Theory]
    [InlineData(SoundCue.VeryRareChime)]
    [InlineData(SoundCue.LegendaryChord)]
    [InlineData(SoundCue.DurabilityWarning)]
    public void ProducesValidPcmWav(SoundCue cue)
    {
        var wav = WavSynth.Render(cue, 0.8);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(WavSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));
    }

    [Fact]
    public void Volume_ScalesAmplitude_AndNeverClips()
    {
        static int Peak(byte[] wav)
        {
            int peak = 0;
            for (int i = 44; i + 1 < wav.Length; i += 2) peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
            return peak;
        }
        Assert.Equal(0, Peak(WavSynth.Render(SoundCue.LegendaryChord, 0)));
        var quiet = Peak(WavSynth.Render(SoundCue.LegendaryChord, 0.2));
        var loud = Peak(WavSynth.Render(SoundCue.LegendaryChord, 1.0));
        Assert.True(loud > quiet * 2);
        Assert.True(loud < short.MaxValue); // мягкий лимитер, без жёсткого клиппинга
    }

    [Fact]
    public void LegendaryChord_IsLongerAndDeeperThanChime()
    {
        Assert.True(WavSynth.Render(SoundCue.LegendaryChord, 1).Length > WavSynth.Render(SoundCue.VeryRareChime, 1).Length);
    }
}
