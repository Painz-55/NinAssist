using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Metrics;
using NinOnlineAutoAssist.Core.SubPunish;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

/// <summary>Wires a fully simulated stack (clock, world, controllers, engines) for tests.</summary>
public sealed class TestRig
{
    public SimulatedClock Clock { get; } = new(0);
    public SimulatedGameWorld World { get; }
    public SimulatedOrientationController Orientation { get; }
    public SimulatedActionExecutor Executor { get; }
    public AutoTurningEngine AutoTurning { get; }
    public SubstitutionManager Substitution { get; }
    public SubTriggerVerifier Verifier { get; }
    public CounterAttackEngine Counter { get; }
    public AttackDetectionEngine Detection { get; }
    public SubPunishEngine SubPunish { get; }
    public MetricsCollector Metrics { get; }

    public TestRig(
        AutoTurningOptions? at = null,
        SubPunishOptions? sp = null,
        SubTriggerVerificationOptions? verify = null,
        CounterAttackOptions? ca = null,
        SubstitutionOptions? sub = null,
        AttackDetectionOptions? det = null)
    {
        World = new SimulatedGameWorld(Clock);
        Orientation = new SimulatedOrientationController(Clock) { FacingSink = d => World.PlayerFacing = d };
        Executor = new SimulatedActionExecutor(Clock);
        AutoTurning = new AutoTurningEngine(Orientation, Clock, options: at ?? new AutoTurningOptions { Enabled = true, MinCommandIntervalMs = 0 });
        Substitution = new SubstitutionManager(Executor, Clock, options: sub ?? new SubstitutionOptions());
        Verifier = new SubTriggerVerifier(Clock, options: verify ?? new SubTriggerVerificationOptions());
        Counter = new CounterAttackEngine(Executor, Clock, options: ca ?? DefaultCounterSequence());
        Detection = new AttackDetectionEngine(Clock, options: det ?? new AttackDetectionOptions());
        Metrics = new MetricsCollector(Clock);
        SubPunish = new SubPunishEngine(Orientation, Clock, Detection, Substitution, Verifier, Counter,
            options: sp ?? new SubPunishOptions { Enabled = true }, metrics: Metrics);
    }

    public static CounterAttackOptions DefaultCounterSequence() => new()
    {
        AttackSequence = new List<CounterAttackAction>
        {
            new() { Id = "basic_attack", Key = "Space", MaxRange = 4.0 },
            new() { Id = "jutsu_slot_1", Key = "1", MaxRange = 6.0, CastTimeMs = 400, ResourceCost = 10, CooldownMs = 4000 },
            new() { Id = "jutsu_slot_2", Key = "2", MaxRange = 8.0, CastTimeMs = 800, ResourceCost = 25, CooldownMs = 8000 },
        },
        InterActionIntervalMs = 100,
        GlobalCooldownMs = 0
    };

    /// <summary>Standard arena: player at origin, attacker "atk" 3 units above, selected & targeted.</summary>
    public void StandardArena(double attackerX = 0, double attackerY = -3)
    {
        World.SpawnTarget("atk", attackerX, attackerY);
        World.SelectTarget("atk");
        World.PlayerFacing = Direction.Down;
    }

    /// <summary>Drain simulated events into the SubPunish engine; optionally tick afterwards.</summary>
    public void Pump(bool tick = true)
    {
        foreach (var evt in World.DrainDueEvents())
            SubPunish.HandleEvent(evt, World.GetSnapshot());
        if (tick) SubPunish.Tick(World.GetSnapshot()!);
    }

    /// <summary>Advance time in steps, pumping each step, until predicate holds or max iterations.</summary>
    public bool AdvanceUntil(Func<bool> predicate, long stepMs = 100, int maxSteps = 50)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            if (predicate()) return true;
            Clock.Advance(stepMs);
            Pump();
        }
        return predicate();
    }

    /// <summary>
    /// Drive the happy path up to CounterattackReady WITHOUT entering the counter-attack.
    /// After this call the state is exactly SubPunishState.CounterattackReady.
    /// </summary>
    public void DriveToCounterattackReady()
    {
        StandardArena();
        World.SimulateThreat("atk", distance: 3.0, confidence: 0.9);
        Pump(false);                              // -> SubActivationRequested
        World.SimulateBuffConfirmed(durationSec: 7.0);
        Pump(false);                              // -> SubActive
        World.SimulateAttackLanded("atk");
        Pump(false);                              // -> SubTriggered -> RepositioningConfirmed -> (turn requested) or Ready
        for (var i = 0; i < 10 && SubPunish.State == SubPunishState.TurningToAttacker; i++)
            SubPunish.Tick(World.GetSnapshot()!); // align -> CounterattackReady (facing applied via sink)
    }
}
