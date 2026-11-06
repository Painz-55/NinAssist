using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class FanAttackTests
{
    private static LiveFrame Frame(double x, double y, Direction direction = Direction.Right, long at = 0) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", Facing = direction },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = x, PositionY = y } }, at, 51, "Local", "Target", false, null, 0);
    private static CombatAssistSettings Settings(AutoAttackMode mode) => new() { AttackMode = mode,
        WeaponRadius = 1.5, FanRange = 6, FanHalfWidth = 0.6, HisuyFanRange = 3, HisuyFanHalfWidth = 1,
        ToolRange = 4, ToolHalfWidth = 0.3, AttackIntervalMs = 200 };

    [Theory]
    [InlineData(AutoAttackMode.Fan, Direction.Up, 0.2, -2.5)]
    [InlineData(AutoAttackMode.Fan, Direction.Down, 0.2, 2.5)]
    [InlineData(AutoAttackMode.Fan, Direction.Left, -2.5, 0.2)]
    [InlineData(AutoAttackMode.Fan, Direction.Right, 2.5, 0.2)]
    [InlineData(AutoAttackMode.HisuyFan, Direction.Up, 0.9, -2.5)]
    [InlineData(AutoAttackMode.HisuyFan, Direction.Down, 0.9, 2.5)]
    [InlineData(AutoAttackMode.HisuyFan, Direction.Left, -2.5, 0.9)]
    [InlineData(AutoAttackMode.HisuyFan, Direction.Right, 2.5, 0.9)]
    public void RangedAttackUsesForwardCorridorInAllFourDirections(AutoAttackMode mode, Direction facing, double x, double y)
    {
        Assert.True(CombatGeometry.InRange(Frame(x, y, facing), CombatAction.Attack, Settings(mode)));
        Assert.Equal(mode == AutoAttackMode.Fan, CombatGeometry.InRange(Frame(x, y, facing), CombatAction.Tool, Settings(mode)));
    }

    [Theory]
    [InlineData(AutoAttackMode.Fan)]
    [InlineData(AutoAttackMode.HisuyFan)]
    public void RangedProfilesNeverAttackBehindSidewaysOrWithUnknownFacing(AutoAttackMode mode)
    {
        foreach (var frame in new[] { Frame(-1, 0), Frame(0, 0), Frame(0, 1), Frame(1, 0, Direction.None) })
            Assert.False(CombatGeometry.InRange(frame, CombatAction.Attack, Settings(mode)));
    }

    [Fact]
    public void FanUsesExactToolBandAndHisuyHasItsOwnBand()
    {
        var fan = Settings(AutoAttackMode.Fan);
        foreach (var frame in new[] { Frame(4, 0.3), Frame(4.01, 0), Frame(2, 0.31), Frame(-1, 0), Frame(2, 0, Direction.None) })
            Assert.Equal(CombatGeometry.InRange(frame, CombatAction.Tool, fan), CombatGeometry.InRange(frame, CombatAction.Attack, fan));
        Assert.False(CombatGeometry.InRange(Frame(6, 0), CombatAction.Attack, fan));
        Assert.True(CombatGeometry.InRange(Frame(4, 0.3), CombatAction.Attack, fan with { FanRange = 1, FanHalfWidth = 0.01 }));
        Assert.True(CombatGeometry.InRange(Frame(3, 1), CombatAction.Attack, Settings(AutoAttackMode.HisuyFan)));
        Assert.False(CombatGeometry.InRange(Frame(3.01, 0), CombatAction.Attack, Settings(AutoAttackMode.HisuyFan)));
        Assert.False(CombatGeometry.InRange(Frame(2, 1.01), CombatAction.Attack, Settings(AutoAttackMode.HisuyFan)));
    }

    [Fact]
    public void MeleeRemainsRadialAndModeDoesNotAlterToolGeometry()
    {
        Assert.True(CombatGeometry.InRange(Frame(-1, 0), CombatAction.Attack, Settings(AutoAttackMode.Melee)));
        foreach (var mode in Enum.GetValues<AutoAttackMode>())
        {
            Assert.True(CombatGeometry.InRange(Frame(4, 0.3), CombatAction.Tool, Settings(mode)));
            Assert.False(CombatGeometry.InRange(Frame(4.01, 0), CombatAction.Tool, Settings(mode)));
            Assert.False(CombatGeometry.InRange(Frame(2, 0.31), CombatAction.Tool, Settings(mode)));
        }
    }

    [Fact]
    public void FanUsesTheSameConfiguredAttackKeyAndTimerNotThrowWeapon()
    {
        var clock = new SimulatedClock(0); var sent = new List<(CombatAction Action, string Key)>();
        var engine = new AutoAttackToolEngine(clock, Settings(AutoAttackMode.Fan) with { UseTool = false }, (action, key) =>
        { sent.Add((action, key)); return ActionResult.Ok(); }, (frame, desired) =>
            frame.Snapshot.Player.Facing == desired ? ActionResult.Ok() : ActionResult.Deferred("aguardando giro"));
        engine.Update(Frame(3, 0), true, false);
        clock.Advance(100); engine.Update(Frame(3, 0, at: 100), true, false);
        Assert.Single(sent);
        clock.Advance(100); engine.Update(Frame(3, 0, at: 200), true, false);
        Assert.Equal(new[] { (CombatAction.Attack, "A"), (CombatAction.Attack, "A") }, sent);
    }

    [Fact]
    public void HisuyAttackTurnsFirstAndWaitsForObservedFacing()
    {
        var clock = new SimulatedClock(100);
        var history = new List<string>();
        long? turnedAt = null;
        var engine = new AutoAttackToolEngine(clock, Settings(AutoAttackMode.HisuyFan) with { UseTool = false }, (action, key) =>
        { history.Add(key); return ActionResult.Ok(); }, (frame, desired) =>
        {
            if (turnedAt is { } at && frame.CapturedAtMs <= at) return ActionResult.Deferred("nova leitura");
            if (frame.Snapshot.Player.Facing == desired) return ActionResult.Ok();
            history.Add($"Turn {desired}"); turnedAt = clock.NowMs;
            return ActionResult.Deferred("aguardando direção");
        });
        // Target to the left is in range only after a real left-facing observation.
        engine.Update(Frame(-2, 0, at: 100), true, false);
        engine.Update(Frame(-2, 0, Direction.Left, at: 100), true, false);
        Assert.Equal(new[] { "Turn Left" }, history);
        clock.Advance(1);
        engine.Update(Frame(-2, 0, Direction.Left, at: 101), true, false);
        Assert.Equal(new[] { "Turn Left", "A" }, history);
    }

    [Fact]
    public void NoTurnOrAttackForDiagonalOutOfBandTargetAndMeleeRemainsRadial()
    {
        var fan = Settings(AutoAttackMode.Fan) with { UseTool = false };
        Assert.Equal(Direction.None, CombatGeometry.AttackDirection(Frame(2, 2), fan));
        Assert.Equal(Direction.None, CombatGeometry.AttackDirection(Frame(-2, 0), fan));
        var sent = new List<string>();
        var engine = new AutoAttackToolEngine(new SimulatedClock(0), fan, (action, key) =>
        { sent.Add(key); return ActionResult.Ok(); }, (_, _) => ActionResult.Rejected("giro bloqueado"));
        engine.Update(Frame(-2, 0), true, false);
        engine.Update(Frame(2, 2), true, false);
        Assert.Empty(sent);
        var melee = new AutoAttackToolEngine(new SimulatedClock(0), Settings(AutoAttackMode.Melee) with { UseTool = false },
            (action, key) => { sent.Add(key); return ActionResult.Ok(); });
        melee.Update(Frame(-1, 0), true, false);
        Assert.Equal(new[] { "A" }, sent);
    }

    [Fact]
    public void FanLikeToolDoesNotRequireTurnPreparationButNeverAttacksBehind()
    {
        var sent = new List<string>();
        var engine = new AutoAttackToolEngine(new SimulatedClock(0), Settings(AutoAttackMode.Fan) with { UseTool = false },
            (_, key) => { sent.Add(key); return ActionResult.Ok(); });
        engine.Update(Frame(2, 0), true, false);
        engine.Update(Frame(-2, 0), true, false);
        Assert.Equal(new[] { "A" }, sent);
    }

    [Fact]
    public void InvalidProfilesAreRejectedAndNeverFallbackToMelee()
    {
        foreach (var settings in new[] { Settings((AutoAttackMode)99), Settings(AutoAttackMode.Fan) with { FanRange = double.NaN },
            Settings(AutoAttackMode.Fan) with { FanHalfWidth = 0 }, Settings(AutoAttackMode.HisuyFan) with { HisuyFanRange = 0 },
            Settings(AutoAttackMode.HisuyFan) with { HisuyFanHalfWidth = double.PositiveInfinity } })
            Assert.Throws<ArgumentException>(settings.Validate);
        Assert.False(CombatGeometry.InRange(Frame(1, 0), CombatAction.Attack, Settings((AutoAttackMode)99)));
    }

    [Fact]
    public void OldConfigsDefaultToMeleeAndBothRangedProfilesKeepIndependentSavedValues()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nin-fan-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            var config = ConfigLoader.Load(path);
            Assert.Equal(AutoAttackMode.Melee, config.CombatAssist.AttackMode);
            ConfigLoader.Save(path, config with { CombatAssist = Settings(AutoAttackMode.HisuyFan) with
                { FanRange = 8, FanHalfWidth = 0.8, HisuyFanRange = 2.5, HisuyFanHalfWidth = 1.3 } });
            var saved = ConfigLoader.Load(path).CombatAssist;
            Assert.Equal(AutoAttackMode.HisuyFan, saved.AttackMode);
            Assert.Equal(8, saved.FanRange); Assert.Equal(0.8, saved.FanHalfWidth);
            Assert.Equal(2.5, saved.HisuyFanRange); Assert.Equal(1.3, saved.HisuyFanHalfWidth);
        }
        finally { File.Delete(path); }
    }
}
