using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace Modern.Tests;

public class CastTurnRearmTests
{
    private static CastObservation Idle => new(false, 0, 0, 0, 0);
    private static CastObservation Active(long remaining, long start = 1000) => new(true, 42, start, 1000, remaining);
    private static LiveFrame Frame(long at, CastObservation cast) => new(new GameSnapshot
    {
        Player = new PlayerState { Id = "local", Facing = Direction.Down },
        Target = new TargetInfo { Id = "target", IsTargeted = true, PositionY = -5 }
    }, at, 51, "Local", "Target", false, null, 0) { Cast = cast };

    [Fact]
    public void EnabledModuleRetainsObservedIdleAcrossTransientGateButNeverTurnsMidCast()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new AutoCastTurnSettings { LeadTimeMs = 100 },
            _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), false, false, enabledWhileBlocked: true);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(150)), true, false);
        clock.Advance(100); engine.Update(Frame(300, Active(50)), true, false);
        Assert.Equal(1, calls);

        engine.Reset(); // OFF/F12/permission loss cannot inherit the earlier idle observation.
        clock.Advance(100); engine.Update(Frame(400, Idle), false, false);
        clock.Advance(100); engine.Update(Frame(500, Active(250, 2000)), true, false);
        clock.Advance(100); engine.Update(Frame(600, Active(150, 2000)), true, false);
        clock.Advance(100); engine.Update(Frame(700, Active(50, 2000)), true, false);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void BlockedActiveCastDoesNotArmEvenWhenModuleIsOn()
    {
        var clock = new SimulatedClock(0); var calls = 0;
        var engine = new AutoCastTurnEngine(clock, new AutoCastTurnSettings { LeadTimeMs = 100 },
            _ => { calls++; return TurnResult.Ok(); });
        engine.Update(Frame(0, Idle), false, false, enabledWhileBlocked: true);
        clock.Advance(50); engine.Update(Frame(50, Active(350)), false, false, enabledWhileBlocked: true);
        clock.Advance(100); engine.Update(Frame(100, Active(250)), true, false);
        clock.Advance(100); engine.Update(Frame(200, Active(150)), true, false);
        clock.Advance(100); engine.Update(Frame(300, Active(50)), true, false);
        Assert.Equal(0, calls);
    }
}
