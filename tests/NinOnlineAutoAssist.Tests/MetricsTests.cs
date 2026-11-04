using NinOnlineAutoAssist.Core.Metrics;
using NinOnlineAutoAssist.Core.SubPunish;
using NinOnlineAutoAssist.Simulation;
using Xunit;

namespace NinOnlineAutoAssist.Tests;

public class MetricsTests
{
    [Fact]
    public void Happy_path_records_all_timing_milestones()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.Completed, 400, 20));

        Assert.True(rig.Metrics.LastDetectionToActivationMs >= 0);
        Assert.True(rig.Metrics.LastActivationToConfirmMs >= 0);
        Assert.True(rig.Metrics.LastConfirmToRepositionMs >= 0);
        Assert.True(rig.Metrics.LastRepositionToFirstActionMs >= 0);
        Assert.Equal(1.0, rig.Metrics.CompletionRate);
        Assert.Equal(0.0, rig.Metrics.FalsePositiveRate);
    }

    [Fact]
    public void Unconfirmed_teleport_counts_as_false_positive_and_lowers_completion()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.SuppressTeleportEvidence = true;
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();
        rig.World.SimulateAttackLanded("atk");
        rig.Pump();
        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.Cancelled, 200, 30));

        Assert.Equal(1, rig.Metrics.FalsePositives);
        Assert.Equal(0, rig.Metrics.TruePositives);
        Assert.Equal(1.0, rig.Metrics.FalsePositiveRate);
        Assert.Equal(0.0, rig.Metrics.CompletionRate);
    }

    [Fact]
    public void Summary_is_human_readable_and_non_empty()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        Assert.False(string.IsNullOrWhiteSpace(rig.Metrics.Summary()));
        Assert.Contains("threats=", rig.Metrics.Summary());
    }
}
