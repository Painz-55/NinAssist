using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class OwnProjectileFollowTests
{
    private static ProjectileSourceDefinition Shuriken => new("Shuriken", 4, true, 2);

    [Theory]
    [InlineData("Shuriken", 2)]
    [InlineData("Fan I Basic", 27)]
    [InlineData("Fan III DMG Range", 27)]
    [InlineData("Fan VIII Triple Short", 27)]
    [InlineData("Fan Jinbu Double Hit", 80)]
    public void IdentifiedLocalWalkingShotsAreNotNavigationHazards(string name, int picture)
        => Assert.True(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, picture, new(name, 4, true, picture)));

    [Theory]
    [InlineData("Bear Trap Technique")]
    [InlineData("Triple Explosive Tag Technique")]
    [InlineData("Unknown Projectile")]
    [InlineData("Fan 999")]
    [InlineData("Fire Fang Technique")]
    [InlineData("Shuriken (NPC)")]
    public void EvenOwnTrapsAndUnmappedSourcesRemainHazards(string name)
        => Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, 2, new(name, 4, true, 2)));

    [Theory]
    [InlineData(34, 35, 1)]
    [InlineData(34, 34, 2)]
    [InlineData(34, 34, 0)]
    [InlineData(0, 0, 1)]
    public void OtherPlayersNpcAndUnknownOwnershipAreNeverExempted(int local, int owner, int ownerType)
        => Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(local, owner, ownerType, 20, 50, 2, Shuriken));

    [Fact]
    public void IncompleteOrMismatchedDefinitionCannotMakeAProjectileSafe()
    {
        Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, 2, null));
        Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, 3, Shuriken));
        Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, 2, Shuriken with { WalkCast = false }));
        Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, 2, Shuriken with { Type = 1 }));
        Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 0, 50, 2, Shuriken));
        Assert.False(ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 0, 2, Shuriken));
    }

    private static LiveFrame Frame(long at, double x = 1.2) => new(new GameSnapshot
    {
        Player = new PlayerState { Id = "34:local", PositionX = x, PositionY = 4.2 },
        Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = 7.2, PositionY = 4.2 }
    }, at, 51, "Local", "Target", false, null, 0);
    private static NavigationFrame Grid(long at, bool blocked = false)
    {
        var cells = Enumerable.Repeat(NavigationCell.Open, 81).ToArray();
        if (blocked) for (var y = 0; y < 9; y++) cells[y * 9 + 4] = NavigationCell.Hazard;
        return new(51, at, 0, 0, 9, 9, cells, true, blocked ? 1 : 0);
    }

    [Fact]
    public void RepeatedOwnShurikensDoNotPulseTheHeldFollowKeyOrLatchAStop()
    {
        var clock = new SimulatedClock(0); var physical = new HashSet<Direction>(); var downs = 0; var ups = 0;
        using var held = new HeldDirectionController(physical.Contains,
            d => { downs++; return physical.Add(d); }, d => { ups++; return physical.Remove(d); });
        var follow = new AutoFollowEngine(clock, new CombatAssistSettings(), s =>
            held.Set(s.Direction, true, s.SecondaryDirection) ? ActionResult.Ok() : ActionResult.Rejected("blocked"), held.Release);
        for (var at = 0; at <= 2000; at += 40)
        {
            clock.Advance(at - clock.NowMs);
            var hazard = !ProjectileHazardPolicy.IgnoreOwnAttack(34, 34, 1, 20, 50, 2, Shuriken);
            follow.Update(Frame(at, 1.2 + at * 0.001), Grid(at, hazard), true, false);
        }
        Assert.Equal(1, downs); Assert.Equal(0, ups); Assert.True(physical.SetEquals(new[] { Direction.Right }));
        follow.Update(Frame(clock.NowMs), Grid(clock.NowMs), false, false);
        Assert.Equal(1, ups); Assert.Empty(physical);
    }

    [Fact]
    public void LongTemporaryHazardStopsSafelyButResumesOnceRouteClearsWithoutReholdingActivation()
    {
        var clock = new SimulatedClock(0); var calls = 0; var releases = 0;
        var follow = new AutoFollowEngine(clock, new CombatAssistSettings(), _ => { calls++; return ActionResult.Ok(); }, () => releases++);
        follow.Update(Frame(0), Grid(0), true, false);
        var initial = calls;
        for (var at = 100; at <= 2500; at += 100)
        { clock.Advance(at - clock.NowMs); follow.Update(Frame(at), Grid(at, true), true, false); }
        Assert.Equal(initial, calls); Assert.True(releases > 0);
        clock.Advance(100); follow.Update(Frame(2600), Grid(2600), true, false);
        Assert.Equal(initial + 1, calls);
    }
}
