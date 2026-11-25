using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace Modern.Tests;

public class NearestTargetTests
{
    private static LiveFrame Frame(long at, string? selected = null, bool stale = false)
    {
        var players = new[]
        {
            new MapPlayer(1, "Local", 51, 0, 0, true, false) { IsAlive = true, Village = PlayerVillage.Leaf },
            new MapPlayer(2, "Dead", 51, 1, 0, false, false) { IsAlive = false, Village = PlayerVillage.Sand },
            new MapPlayer(3, "Ally", 51, 2, 0, false, false) { IsAlive = true, Village = PlayerVillage.Leaf },
            new MapPlayer(4, "Nearest", 51, 3, 0, false, false) { IsAlive = true, Village = PlayerVillage.Sand },
            new MapPlayer(5, "Far", 51, 8, 0, false, false) { IsAlive = true, Village = PlayerVillage.Sand },
        };
        return new LiveFrame(new GameSnapshot
        {
            Player = new PlayerState { Id = "1:Local", Facing = Direction.Down },
            Target = selected is null ? null : new TargetInfo { Id = selected, IsTargeted = true }
        }, at, 51, "Local", selected, false, null, 0)
        {
            Cast = new CastObservation(false, 0, 0, 0, 0),
            MapPlayers = new MapPlayersFrame(51, "1:Local", stale ? at - 700 : at, players, 0)
        };
    }

    [Fact]
    public void ClosestLivingEnemyIsChosenWithinRadius()
    {
        Assert.Equal("4:Nearest", NearestTargetPolicy.Resolve(Frame(100), 4, true)?.Id);
        Assert.Equal("3:Ally", NearestTargetPolicy.Resolve(Frame(100), 4, false)?.Id);
        Assert.Null(NearestTargetPolicy.Resolve(Frame(100), 2, true));
        Assert.Null(NearestTargetPolicy.Resolve(Frame(1000, stale: true), 12, true));
    }

    [Fact]
    public void IncompleteMapOrUnknownHealthFailsClosed()
    {
        var frame = Frame(100);
        Assert.Null(NearestTargetPolicy.Resolve(frame with { MapPlayers = frame.MapPlayers! with { UnreadableSlots = 1 } }, 12, true));
        var unknown = frame.MapPlayers!.Players.Select(p => p with { IsAlive = false }).ToArray();
        Assert.Null(NearestTargetPolicy.Resolve(frame with { MapPlayers = frame.MapPlayers! with { Players = unknown } }, 12, true));
        var villageUnknown = frame.MapPlayers!.Players.Select(p => p.IsLocal ? p with { Village = PlayerVillage.Unknown } : p).ToArray();
        Assert.Null(NearestTargetPolicy.Resolve(frame with { MapPlayers = frame.MapPlayers! with { Players = villageUnknown } }, 12, true));
    }

    [Fact]
    public void TargetSelectionIsBoundedAndRequiresFreshObservation()
    {
        var clock = new SimulatedClock(0);
        var taps = 0;
        var engine = new AutoTargetEngine(clock, new AutoTargetSettings { MaxAttempts = 2, AttemptIntervalMs = 200 },
            _ => { taps++; return ActionResult.Ok(); });
        engine.Update(Frame(0), true, true, false);
        clock.Advance(250);
        engine.Update(Frame(0), true, true, false);
        Assert.Equal(1, taps);
        engine.Update(Frame(250), true, true, false);
        clock.Advance(250);
        engine.Update(Frame(500), true, true, false);
        Assert.Equal(2, taps);
        engine.Update(Frame(500, "4:Nearest"), true, true, false);
        Assert.Contains("selecionado", engine.Status);
        engine.Update(Frame(500), false, false, false);
        Assert.Equal("Auto Target OFF", engine.Status);
    }

    [Fact]
    public void SmartTargetOnlyRequestsWhileHeldAndPreservesLastSelectionOnRelease()
    {
        var settings = new SmartTargetSettings { RadiusTiles = 5, HoldKey = "Tab" };
        settings.Validate();
        Assert.True(settings.Selection().IgnoreSameVillage);
        Assert.Throws<ArgumentException>(() => (settings with { HoldKey = "F12" }).Validate());
        var clock = new SimulatedClock(0);
        var selected = new List<string>();
        var engine = new AutoTargetEngine(clock, settings.Selection(), target =>
        {
            selected.Add(target.Id);
            return ActionResult.Ok();
        }, "Smart Target");
        engine.Update(Frame(0), enabled: false, allowed: true, higherPriorityBusy: false);
        Assert.Empty(selected);
        engine.Update(Frame(0), enabled: true, allowed: true, higherPriorityBusy: false);
        Assert.Equal(new[] { "4:Nearest" }, selected);
        engine.Update(Frame(100, "4:Nearest"), enabled: false, allowed: false, higherPriorityBusy: false);
        Assert.Single(selected);
        Assert.Equal("Smart Target OFF", engine.Status);
        clock.Advance(200);
        engine.Update(Frame(200, "4:Nearest"), enabled: true, allowed: true, higherPriorityBusy: false);
        Assert.Single(selected);
        Assert.Contains("selecionado", engine.Status);
    }

    [Fact]
    public void NearestCastTurnDoesNotRequireManualTarget()
    {
        var clock = new SimulatedClock(0);
        var turns = new List<Direction>();
        var settings = new AutoCastTurnSettings { TargetMode = CastTurnTargetMode.Nearest, LeadTimeMs = 100 };
        var engine = new AutoCastTurnEngine(clock, settings, direction =>
        {
            turns.Add(direction);
            return TurnResult.Ok();
        });
        engine.Update(Frame(0), true, false);
        clock.Advance(100);
        engine.Update(Frame(100) with { Cast = new CastObservation(true, 42, 1000, 1000, 250) }, true, false);
        clock.Advance(100);
        engine.Update(Frame(200) with { Cast = new CastObservation(true, 42, 1000, 1000, 150) }, true, false);
        clock.Advance(100);
        engine.Update(Frame(300) with { Cast = new CastObservation(true, 42, 1000, 1000, 50) }, true, false);
        Assert.Equal(new[] { Direction.Right }, turns);
    }

    [Fact]
    public void SafetyGatesPreserveFocusAndEmergencyStop()
    {
        var frame = Frame(100);
        Assert.True(LiveSafetyGate.CheckAutoTarget(frame, 100, true, true, false, false).Allowed);
        Assert.False(LiveSafetyGate.CheckAutoTarget(frame, 100, true, false, false, false).Allowed);
        Assert.False(LiveSafetyGate.CheckAutoTarget(frame, 100, true, true, true, false).Allowed);
        Assert.False(LiveSafetyGate.CheckCastTurning(frame, 100, true, true, false, false).Allowed);
        Assert.True(LiveSafetyGate.CheckCastTurning(frame, 100, true, true, false, false, requiresSelectedTarget: false).Allowed);
    }

    [Fact]
    public void SettingsRejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new AutoTargetSettings { RadiusTiles = double.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new AutoTargetSettings { MaxAttempts = 0 }.Validate());
        Assert.Throws<ArgumentException>(() => new AutoCastTurnSettings { NearestRadiusTiles = 0 }.Validate());
    }

    [Fact]
    public void MemoryWriterRefusesUnsignedOrUnapprovedProfilesBeforeOpeningProcess()
    {
        var writer = new TargetMemoryWriter();
        var frame = Frame(100);
        var candidate = NearestTargetPolicy.Resolve(frame, 12, true)!;
        var profile = new GameMemoryProfile { Id = "test", Sha256 = "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634",
            CommonSha256 = "test", SelectedTarget = new SelectedTargetMemoryFields("obj", "kind", "index", 1) };
        Assert.False(writer.Select(-1, profile, frame, candidate, () => true).Accepted);
        Assert.False(writer.Select(-1, profile with { ExternalProfile = true }, frame, candidate, () => true).Accepted);
        Assert.False(writer.Select(-1, profile with { ExternalProfile = true, ValidatedInputKinds = ["TargetMemory"] }, frame, candidate, () => false).Accepted);
    }

}
