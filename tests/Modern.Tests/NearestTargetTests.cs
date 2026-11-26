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
    public void MaskedAppearanceDoesNotOverrideExplicitVillageFilterOrLifeAndRadius()
    {
        var frame = Frame(100);
        var masked = frame.MapPlayers!.Players.Select(p => p.Slot == 3
            ? p with { Name = "Masked", Village = PlayerVillage.Leaf } : p).ToArray();
        frame = frame with { MapPlayers = frame.MapPlayers with { Players = masked } };
        Assert.Equal("3:Masked", NearestTargetPolicy.Resolve(frame, 12, false)?.Id);
        Assert.Equal("4:Nearest", NearestTargetPolicy.Resolve(frame, 12, true)?.Id);
        Assert.Null(NearestTargetPolicy.Resolve(frame, 1, false, out string radiusReason));
        Assert.Contains("fora do raio", radiusReason);
        var noHealth = masked.Select(p => p.IsLocal ? p : p with { IsAlive = false }).ToArray();
        Assert.Null(NearestTargetPolicy.Resolve(frame with { MapPlayers = frame.MapPlayers with { Players = noHealth } }, 12, false, out string healthReason));
        Assert.Contains("vida não confirmada", healthReason);
        Assert.Null(NearestTargetPolicy.Resolve(frame with { MapPlayers = frame.MapPlayers with { UnreadableSlots = 1 } }, 12, false, out string slotsReason));
        Assert.Contains("lista incompleta", slotsReason);
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
    public void AutoTargetRequestsAgainAfterEscapeClearsConfirmedSelection()
    {
        var clock = new SimulatedClock(0);
        var requested = new List<string>();
        var engine = new AutoTargetEngine(clock, new AutoTargetSettings { MaxAttempts = 2, AttemptIntervalMs = 200 },
            target => { requested.Add(target.Id); return ActionResult.Ok(); });
        engine.Update(Frame(0), enabled: true, allowed: true, higherPriorityBusy: false);
        Assert.Single(requested);
        clock.Advance(250);
        engine.Update(Frame(250, "4:Nearest"), enabled: true, allowed: true, higherPriorityBusy: false);
        Assert.Contains("selecionado", engine.Status);
        // The player presses Esc; a new frame with no selected target can re-arm the normal engine.
        clock.Advance(250);
        engine.Update(Frame(500), enabled: true, allowed: true, higherPriorityBusy: false);
        Assert.Equal(2, requested.Count);
        Assert.Equal("4:Nearest", requested[1]);
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

    [Fact]
    public void MemoryWriterRefusesCurrentFourIntegerBuildEvenWithClaimedApproval()
    {
        using var writer = new TargetMemoryWriter();
        var frame = Frame(100);
        var candidate = NearestTargetPolicy.Resolve(frame, 12, true)!;
        var profile = new GameMemoryProfile
        {
            Id = "unapproved four-integer layout",
            Sha256 = "8A163FC3D8FA13CCB3AA1FBABD9D1EBA1C829FBAE4ED14CC05AC688CAD328D50",
            CommonSha256 = "43B99E8E93F063DFA1239978C7B41C51520E765CAB8D21499160FCA14CD01F62",
            ExternalProfile = true,
            ActuationValidated = true,
            ValidatedInputKinds = ["TargetMemory"],
            SelectedTarget = new SelectedTargetMemoryFields("F3miMmd6RE4", "dxFYxym9RQk", "okyYx7CwjXs", 1)
        };
        // Must reject before process attachment and before calling the dynamic authorization gate.
        Assert.False(writer.Select(-1, profile, frame, candidate, () => throw new Exception("gate called")).Accepted);
    }

    [Fact]
    public void LocalTargetTrialCannotEnableOtherInputsOrWrongBuild()
    {
        var profile = new GameMemoryProfile
        {
            Id = "LOCAL TRIAL Raizen TargetMemory",
            Sha256 = "8A163FC3D8FA13CCB3AA1FBABD9D1EBA1C829FBAE4ED14CC05AC688CAD328D50",
            CommonSha256 = "43B99E8E93F063DFA1239978C7B41C51520E765CAB8D21499160FCA14CD01F62",
            ExternalProfile = true, ExperimentalTrial = true, ActuationValidated = false,
            ValidatedInputKinds = ["TargetMemory"], ValidatedAttackModes = [], PunishComboValidated = false
        };
        Assert.True(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(profile));
        Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(profile with { ValidatedInputKinds = ["TargetMemory", "Attack"] }));
        Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(profile with { CommonSha256 = new string('0', 64) }));
        Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(profile with { ExperimentalTrial = false }));
        Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(profile with { Id = "Official" }));
        var integrated = profile with { ValidatedInputKinds = ["Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory"],
            ValidatedAttackModes = ["Melee", "Fan"], PunishComboValidated = true, PunishComboRequiresTarget = true };
        Assert.True(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(integrated));
        Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(integrated with { ValidatedAttackModes = ["Melee", "Fan", "HisuyFan"] }));
		var steam = integrated with { Id = "LOCAL TRIAL Steam Raizen Turning Stamina 2026-11-26",
			Sha256 = "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525",
			CommonSha256 = "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102",
			ValidatedInputKinds = ["Turning", "Stamina"], ValidatedAttackModes = [], PunishComboValidated = false };
		Assert.True(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(steam));
		Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(steam with { ValidatedInputKinds = ["Turning", "Stamina", "TargetMemory"] }));
		Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(steam with { CommonSha256 = new string('0', 64) }));
		var targetOnly = steam with { Id = "LOCAL TRIAL Steam Raizen TargetMemory 2026-11-26", ValidatedInputKinds = ["TargetMemory"] };
		Assert.True(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(targetOnly));
		Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(targetOnly with { ValidatedInputKinds = ["TargetMemory", "Stamina"] }));
		var steamIntegrated = targetOnly with { Id = "LOCAL TRIAL Steam Integrated Raizen 2026-11-26",
			ValidatedInputKinds = ["Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory"],
			ValidatedAttackModes = ["Melee", "Fan"], PunishComboValidated = true, PunishComboRequiresTarget = true };
		Assert.True(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(steamIntegrated));
		Assert.False(NinOnlineAutoAssist.App.LocalTrialPolicy.CanActuate(steamIntegrated with { ValidatedAttackModes = ["Melee", "Fan", "HisuyFan"] }));
    }

}
