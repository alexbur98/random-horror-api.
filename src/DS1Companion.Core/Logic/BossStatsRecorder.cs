using DS1Companion.Core.Game;
using DS1Companion.Core.Persistence;

namespace DS1Companion.Core.Logic;

public static class BossStatsRecorder
{
    public static void RecordStart(CharacterProfile profile, string bossId) => profile.Boss(bossId).Attempts++;

    public static void RecordEnd(CharacterProfile profile, BossFightResult result, DateTime now)
    {
        var r = profile.Boss(result.Boss.Id);
        r.TotalSeconds += result.Duration.TotalSeconds;
        if (result.Victory)
        {
            r.Defeated = true;
            r.DefeatedAt ??= now;
            var secs = result.Duration.TotalSeconds;
            if (r.BestVictorySeconds is null || secs < r.BestVictorySeconds) r.BestVictorySeconds = secs;
        }
        else
        {
            r.Defeats++;
        }
    }
}
