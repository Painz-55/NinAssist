using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class CombatAssistTests
{
    private static readonly CombatAssistSettings Settings = new() { WeaponRadius = 2, ToolRange = 6, ToolHalfWidth = 0.25,
        AttackIntervalMs = 200, ToolIntervalMs = 500 };
    private static LiveFrame Frame(long at = 0, double x = 1, double y = 0, Direction facing = Direction.Right) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", Facing = facing },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = x, PositionY = y } },
        at, 51, "Player", "Target", false, null, 0);

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(-1, -1, true)]
    [InlineData(-2, 0, true)]
    [InlineData(0, 2, true)]
    [InlineData(1.5, 1.5, false)]
    [InlineData(2.01, 0, false)]
    public void AutoAttackUsesRadialRangeIndependentOfFacing(double x, double y, bool expected) =>
        Assert.Equal(expected, CombatGeometry.InRange(Frame(x: x, y: y, facing: Direction.Down), CombatAction.Attack, Settings));

    [Theory]
    [InlineData(0, -5, Direction.Up, true)]
    [InlineData(0, 5, Direction.Down, true)]
    [InlineData(-5, 0, Direction.Left, true)]
    [InlineData(5, 0, Direction.Right, true)]
    [InlineData(5, 0, Direction.Left, false)]
    [InlineData(5, 0.26, Direction.Right, false)]
    [InlineData(5, 0.25, Direction.Right, true)]
    [InlineData(6.01, 0, Direction.Right, false)]
    [InlineData(0, 0, Direction.Right, false)]
    [InlineData(5, 0, Direction.None, false)]
    [InlineData(2, 2, Direction.Right, false)]
    public void ToolUsesOnlyTheNarrowForwardCorridor(double x, double y, Direction facing, bool expected) =>
        Assert.Equal(expected, CombatGeometry.InRange(Frame(x: x, y: y, facing: facing), CombatAction.Tool, Settings));

    [Fact]
    public void IndependentIntervalsDoNotReplayMissedAttacksOrStarveTool()
    {
        var clock = new SimulatedClock(0);
        var sent = new List<(long At, CombatAction Action)>();
        var engine = new AutoAttackToolEngine(clock, Settings, (action, _) =>
        { sent.Add((clock.NowMs, action)); return ActionResult.Ok(); });
        foreach (var at in new long[] { 0, 1, 100, 199, 200, 500, 501, 10000 })
        {
            clock.Advance(at - clock.NowMs); engine.Update(Frame(at), true, false);
        }
        Assert.Equal(new[] { (0L, CombatAction.Attack), (1L, CombatAction.Tool), (200L, CombatAction.Attack),
            (500L, CombatAction.Attack), (501L, CombatAction.Tool), (10000L, CombatAction.Attack) }, sent);
    }

    [Fact]
    public void RefusedInputsAreRateLimitedToo()
    {
        var clock = new SimulatedClock(0);
        var calls = 0;
        var engine = new AutoAttackToolEngine(clock, Settings with { UseTool = false }, (_, _) =>
        { calls++; return ActionResult.Rejected("manual key held"); });
        for (var at = 0; at < 200; at += 10)
        { clock.Advance(at - clock.NowMs); engine.Update(Frame(at), true, false); }
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DisabledPermissionAndSubPunishPriorityPreventAllNewAttackInputs(bool allowed, bool busy)
    {
        var calls = 0;
        var engine = new AutoAttackToolEngine(new SimulatedClock(0), Settings, (_, _) => { calls++; return ActionResult.Ok(); });
        engine.Update(Frame(), allowed, busy);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("unauthorized")]
    [InlineData("focus")]
    [InlineData("emergency")]
    [InlineData("text")]
    [InlineData("stale")]
    [InlineData("dead-target")]
    [InlineData("no-target")]
    public void NewModulesKeepTheirOwnTargetAndPermissionGuards(string invalid)
    {
        var frame = Frame();
        if (invalid == "no-target") frame = frame with { Snapshot = frame.Snapshot with { Target = null } };
        if (invalid == "dead-target") frame = frame with { Snapshot = frame.Snapshot with { Target = frame.Snapshot.Target! with { IsAlive = false } } };
        Assert.False(LiveSafetyGate.CheckTargetedModule(frame, invalid == "stale" ? 501 : 0,
            invalid != "off", invalid != "unauthorized", invalid != "focus", invalid == "emergency", invalid == "text").Allowed);
    }

    [Fact]
    public void MalformedRangeOrKeysCannotBeSaved()
    {
        foreach (var settings in new[] { Settings with { WeaponRadius = double.NaN }, Settings with { ToolHalfWidth = 0 },
            Settings with { AttackKey = "F12" }, Settings with { ToolKey = "Up" }, Settings with { ToolKey = "A" },
            Settings with { FollowHoldKey = "A" }, Settings with { AttackIntervalMs = 0 }, Settings with { FollowStepIntervalMs = 5 } })
            Assert.Throws<ArgumentException>(settings.Validate);
    }

    [Fact]
    public void SettingsRoundTripWithoutEnablingModulesOrGrantingPermission()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ninautoassist-combat-{Guid.NewGuid():N}.json");
        try
        {
            ConfigLoader.Save(path, new AutoAssistConfig { CombatAssist = Settings with { AttackKey = "X", ToolKey = "S" } });
            var config = ConfigLoader.Load(path);
            Assert.Equal(Settings.ToolHalfWidth, config.CombatAssist.ToolHalfWidth);
            Assert.Equal("X", config.CombatAssist.AttackKey);
            Assert.False(config.Live.AllowCounterAttackSequence);
            Assert.False(config.AutoTurning.Enabled);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FirstUnprocessedRemovalPreemptsNewActionsEvenWhenAnotherSubRemainsActive()
    {
        var clock = new SimulatedClock(0);
        var executor = new SimulatedActionExecutor(clock);
        var punish = new EarlyBuffPunishEngine(executor, clock);
        var before = Frame() with { SubstitutionActive = true, SubstitutionDuration = 5,
            ObservedActiveBuffs = new[] { new ObservedBuff(SubstitutionBuffs.Normal, 5), new ObservedBuff(SubstitutionBuffs.Water, 5) } };
        punish.Update(before, true, true, true);
        clock.Advance(10);
        var after = before with { CapturedAtMs = 10, ObservedActiveBuffs = new[] { new ObservedBuff(SubstitutionBuffs.Normal, 5) } };
        Assert.True(punish.HasUnprocessedRemoval(after));
        Assert.Empty(executor.History); // priority is not evidence/permission to execute a combo yet
        punish.Update(after, true, true, true);
        Assert.False(punish.HasUnprocessedRemoval(after));
        Assert.True(punish.BlocksAutoTurning);
        punish.Reset();
        Assert.False(punish.HasUnprocessedRemoval(after));
    }

    [Theory]
    [InlineData("Num2", "2")]
    [InlineData("A", "A")]
    [InlineData("Numpad2", "Numpad2")]
    public void GameDigitBindingNamesAreNormalizedWithoutConfusingNumpad(string source, string expected) =>
        Assert.Equal(expected, LiveKeySettings.NormalizeGameKey(source));
}
