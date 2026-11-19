using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class CombatDispatchFreshnessTests
{
    [Fact]
    public void SlowOrOldFacingSampleCannotAuthorizeProjectile()
    {
        var frame = new LiveFrame(new GameSnapshot { Player = new PlayerState { Id = "75:Raizen Kaen" } }, 1000, 190, "Raizen Kaen", null,
            false, null, 30);
        Assert.True(CombatDispatchFreshness.Allows(frame, 1080));
        Assert.False(CombatDispatchFreshness.Allows(frame, 1081));
        Assert.False(CombatDispatchFreshness.Allows(frame with { ReadDurationMs = 121 }, 1100));
        Assert.False(CombatDispatchFreshness.Allows(null, 1100));
    }

    [Fact]
    public void SlowCensusDefersToolWithoutConsumingItsInterval()
    {
        var clock = new SimulatedClock(200); var calls = 0;
        var settings = new CombatAssistSettings { UseAttack = false, UseTool = true };
        var engine = new AutoAttackToolEngine(clock, settings, (_, _) =>
        { calls++; return ActionResult.Ok(); });
        var frame = new LiveFrame(new GameSnapshot
        {
            Player = new PlayerState { Id = "75:Raizen Kaen", Facing = Direction.Right, IsAlive = true },
            Target = new TargetInfo { Id = "77:target", IsAlive = true, IsTargeted = true, PositionX = 2, PositionY = 0 }
        }, 100, 190, "Raizen Kaen", "target", false, null, 180);
        engine.Update(frame, false, true, false);
        Assert.Equal(0, calls);
        clock.Advance(30);
        engine.Update(frame with { CapturedAtMs = 210, ReadDurationMs = 30 }, false, true, false);
        Assert.Equal(1, calls);
    }
}
