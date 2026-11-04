using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.SubPunish;
using NinOnlineAutoAssist.Simulation;
using Xunit;

namespace NinOnlineAutoAssist.Tests;

public class SubPunishTests
{
    // ── 1. Substitution disponível ────────────────────────────────────
    [Fact]
    public void Substitution_available_full_happy_path_completes()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        // Drive the counter-attack to completion.
        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.Completed, 400, 20));
        Assert.Equal(SubPunishState.Completed, rig.SubPunish.State);
        Assert.Equal(1, rig.Metrics.SubsConfirmed);
        Assert.Equal(1, rig.Metrics.SequencesCompleted);
        // Substitution was requested before the attack.
        Assert.Contains(rig.Executor.History, h => h.ActionId == "substitution");
    }

    // ── 2. Substitution em cooldown ───────────────────────────────────
    [Fact]
    public void Substitution_on_cooldown_never_activates_and_hit_is_cancelled()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.ProviderSubstitutionCooldownMs = 15_000;
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        Assert.Equal(SubPunishState.WaitingForAttack, rig.SubPunish.State);
        Assert.DoesNotContain(rig.Executor.History, h => h.ActionId == "substitution");

        rig.World.SimulateAttackLanded("atk");
        rig.Pump();
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Equal(0, rig.Metrics.ActivationsRequested);
    }

    // ── 3. Ataque detectado antes da ativação ─────────────────────────
    [Fact]
    public void Attack_landing_before_buff_confirmation_cancels_sequence()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        Assert.Equal(SubPunishState.SubActivationRequested, rig.SubPunish.State);

        rig.World.SimulateAttackLanded("atk");   // arrives before BuffConfirmed
        rig.Pump();
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Equal(1, rig.Metrics.AttacksLandedBeforeActivation);
        Assert.Contains("before buff confirmation", rig.SubPunish.CancellationReason);
    }

    // ── 4. Buff ativado corretamente ───────────────────────────────────
    [Fact]
    public void Buff_activation_is_only_counted_after_confirmation()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        Assert.Equal(SubPunishState.SubActivationRequested, rig.SubPunish.State);
        Assert.Equal(0, rig.Metrics.BuffsConfirmed);   // requested ≠ confirmed

        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();
        Assert.Equal(SubPunishState.SubActive, rig.SubPunish.State);
        Assert.Equal(1, rig.Metrics.BuffsConfirmed);
    }

    // ── 5. Buff expira sem receber ataque ──────────────────────────────
    [Fact]
    public void Buff_expiring_without_attack_returns_to_idle()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(1.0);
        rig.Pump();
        Assert.Equal(SubPunishState.SubActive, rig.SubPunish.State);

        rig.Clock.Advance(1100);       // pass natural expiry
        rig.Pump();                    // world emits BuffExpired naturally
        Assert.Equal(SubPunishState.Idle, rig.SubPunish.State);
        Assert.Equal(0, rig.Metrics.SubsConfirmed);
    }

    // ── 6. Substitution acionada ───────────────────────────────────────
    [Fact]
    public void Substitution_trigger_moves_engine_to_verification()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();
        rig.World.SimulateAttackLanded("atk");
        // Do NOT pump yet — inspect mid-flight state after full drain.
        rig.Pump();
        Assert.Contains(rig.SubPunish.State,
            new[] { SubPunishState.RepositioningConfirmed, SubPunishState.TurningToAttacker, SubPunishState.CounterattackReady, SubPunishState.CounterattackExecuting });
        Assert.True(rig.Metrics.SubsConfirmed >= 1);
    }

    // ── 7. Teleporte confirmado ────────────────────────────────────────
    [Fact]
    public void Teleport_confirmed_repositions_behind_attacker()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        double dist = Math.Sqrt(Math.Pow(rig.World.PlayerX - 0, 2) + Math.Pow(rig.World.PlayerY - (-3), 2));
        Assert.True(dist < 2.0, $"Player should be adjacent to attacker, was {dist:F2}u");
        Assert.Equal(1, rig.Metrics.SubsConfirmed);
    }

    // ── 8. Teleporte não confirmado ────────────────────────────────────
    [Fact]
    public void Teleport_not_confirmed_times_out_and_never_counterattacks()
    {
        var rig = new TestRig();
        rig.StandardArena();
        rig.World.SuppressTeleportEvidence = true;   // bare trigger noise, no corroboration
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();
        rig.World.SimulateAttackLanded("atk");
        rig.Pump();
        Assert.Equal(SubPunishState.SubTriggered, rig.SubPunish.State);

        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.Cancelled, 200, 30));
        Assert.Equal("Teleport not confirmed within timeout", rig.SubPunish.CancellationReason);
        Assert.Equal(0, rig.Metrics.SubsConfirmed);
        Assert.True(rig.Metrics.FalsePositives >= 1);
        Assert.DoesNotContain(rig.Executor.History, h => h.ActionId == "basic_attack");
    }

    // ── 9. Atacante diferente do alvo selecionado ──────────────────────
    [Fact]
    public void Attacker_mismatch_cancels_by_default_policy()
    {
        var rig = new TestRig();
        rig.World.SpawnTarget("select", 0, -3);
        rig.World.SelectTarget("select");
        rig.World.SpawnTarget("other", 2, -3);
        rig.World.PlayerFacing = Direction.Down;

        rig.World.SimulateThreat("select", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();

        // Attack lands, but the observed teleport attributes it to "other".
        rig.World.SimulateAttackLanded("other");
        rig.Pump();
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Contains("mismatch", rig.SubPunish.CancellationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attacker_mismatch_can_be_proceeded_with_explicit_policy()
    {
        var rig = new TestRig(sp: new SubPunishOptions
        {
            Enabled = true,
            TargetSelectionPolicy = TargetSelectionPolicy.ProceedWithAttacker
        });
        rig.World.SpawnTarget("select", 0, -3);
        rig.World.SelectTarget("select");
        rig.World.SpawnTarget("other", 0, -4);
        rig.World.SimulateThreat("select", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();
        rig.World.SimulateAttackLanded("other");
        rig.Pump();
        Assert.NotEqual(SubPunishState.Cancelled, rig.SubPunish.State);
    }

    // ── 10. Personagem já orientado corretamente ───────────────────────
    [Fact]
    public void Already_facing_attacker_skips_turning_step()
    {
        var rig = new TestRig();
        rig.StandardArena();              // attacker above; player starts facing Down
        rig.World.PlayerFacing = Direction.Up;
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump(false);
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump(false);
        int turnsBefore = rig.Orientation.RequestCount;
        rig.World.SimulateAttackLanded("atk");
        rig.Pump(false);                     // no tick: observe the exact post-teleport state
        Assert.Equal(SubPunishState.CounterattackReady, rig.SubPunish.State);
        Assert.Equal(turnsBefore, rig.Orientation.RequestCount);  // no turn requested
    }

    // ── 11. Necessidade de corrigir a orientação ───────────────────────
    [Fact]
    public void Misaligned_facing_is_corrected_before_counterattack()
    {
        var rig = new TestRig();
        rig.StandardArena();              // player faces Down, attacker is above
        rig.World.PlayerFacing = Direction.Down;
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump(false);
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump(false);
        rig.World.SimulateAttackLanded("atk");
        rig.Pump(false);
        Assert.Contains(rig.Orientation.History, h => h.Direction == Direction.Up);
        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.CounterattackReady, 50, 10));
    }

    // ── 12. Contra-ataque executado ────────────────────────────────────
    [Fact]
    public void Counter_attack_executes_sequence_in_order()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.Completed, 400, 20));
        var sequence = rig.Executor.History.Select(h => h.ActionId).ToList();
        var subIndex = sequence.IndexOf("substitution");
        Assert.True(subIndex >= 0);
        Assert.Equal(new[] { "basic_attack", "jutsu_slot_1", "jutsu_slot_2" },
                     sequence.Skip(subIndex + 1).ToArray());
    }

    // ── 13. Contra-ataque cancelado por distância ──────────────────────
    [Fact]
    public void Counter_attack_cancelled_when_attacker_out_of_range()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        // Attacker dashes away far beyond every configured MaxRange.
        rig.World.MoveTarget("atk", 0, -500);
        rig.Pump();  // CounterattackReady tick -> CheckWindow fails
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Contains("range", rig.SubPunish.CancellationReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rig.Executor.History, h => h.ActionId == "basic_attack");
    }

    // ── 14. Habilidade indisponível ────────────────────────────────────
    [Fact]
    public void Counter_attack_aborts_when_resources_insufficient()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        rig.World.PlayerResources = 0;   // cannot pay jutsu_slot_1 (10) — but basic_attack is free
        Assert.True(rig.AdvanceUntil(() => rig.SubPunish.State == SubPunishState.Completed
                                            || rig.SubPunish.State == SubPunishState.Cancelled, 400, 20));
        // basic_attack executes; jutsu_slot_1 fails with insufficient resources -> Abort policy
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Contains("Insufficient resources", rig.SubPunish.CancellationReason);
        Assert.DoesNotContain(rig.Executor.History, h => h.ActionId == "jutsu_slot_1");
    }

    // ── 15. Latência elevada ───────────────────────────────────────────
    [Fact]
    public void High_estimated_latency_closes_the_attack_window()
    {
        var rig = new TestRig(ca: new CounterAttackOptions
        {
            AttackSequence = TestRig.DefaultCounterSequence().AttackSequence,
            AttackWindowMs = 200,
            EstimatedLatencyMs = 10_000,   // absurdly high latency — window unusable
            InterActionIntervalMs = 100
        });
        rig.DriveToCounterattackReady();
        rig.Pump();  // CounterattackReady -> CheckWindow fails due to latency-adjusted window
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Contains("window", rig.SubPunish.CancellationReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── 16. Eventos recebidos fora de ordem ────────────────────────────
    [Fact]
    public void Out_of_order_events_are_ignored_or_reconciled()
    {
        var rig = new TestRig();
        rig.StandardArena();

        // Noise before any threat: must be ignored, no crash, stays Idle.
        rig.SubPunish.HandleEvent(CombatEventKindSafe.Create(CombatEventKindSafe.TeleportObserved, "atk", x: 1, y: 1), rig.World.GetSnapshot());
        rig.SubPunish.HandleEvent(CombatEventKindSafe.Create(CombatEventKindSafe.BuffConfirmed, null), rig.World.GetSnapshot());
        rig.Pump();
        Assert.Equal(SubPunishState.Idle, rig.SubPunish.State);

        // Now the real sequence, but with teleport evidence arriving BEFORE the trigger event.
        rig.World.SimulateThreat("atk", distance: 3, confidence: 0.9);
        rig.Pump();
        rig.World.SimulateBuffConfirmed(7.0);
        rig.Pump();

        // World actually teleports the player (3,-4): 5u jump, inside the plausible band.
        rig.World.PlayerX = 3; rig.World.PlayerY = -4;
        rig.SubPunish.HandleEvent(CombatEventKindSafe.Create(CombatEventKindSafe.TeleportObserved, "atk", x: 3, y: -4), rig.World.GetSnapshot());
        rig.SubPunish.HandleEvent(CombatEventKindSafe.Create(CombatEventKindSafe.SubstitutionTriggered, "atk"), rig.World.GetSnapshot());
        rig.Pump();
        Assert.True(rig.Metrics.SubsConfirmed >= 1, "Reordered teleport/trigger evidence should still confirm");
    }

    // ── 17. Interrupção manual ─────────────────────────────────────────
    [Fact]
    public void Manual_interrupt_cancels_at_any_stage()
    {
        var rig = new TestRig();
        rig.DriveToCounterattackReady();
        rig.World.SimulateManualInterrupt();
        rig.Pump();
        Assert.Equal(SubPunishState.Cancelled, rig.SubPunish.State);
        Assert.Equal("Manual interrupt", rig.SubPunish.CancellationReason);
    }
}

/// <summary>Tiny helper to build CombatEvents in tests.</summary>
internal static class CombatEventKindSafe
{
    public const Core.Events.CombatEventKind TeleportObserved = Core.Events.CombatEventKind.TeleportObserved;
    public const Core.Events.CombatEventKind SubstitutionTriggered = Core.Events.CombatEventKind.SubstitutionTriggered;
    public const Core.Events.CombatEventKind BuffConfirmed = Core.Events.CombatEventKind.BuffConfirmed;

    public static Core.Events.CombatEvent Create(Core.Events.CombatEventKind kind, string? attackerId,
        double? x = null, double? y = null) =>
        new()
        {
            Kind = kind,
            TimestampMs = 0,
            Origin = Core.Events.EventOrigin.Simulated,
            AttackerId = attackerId,
            PositionX = x,
            PositionY = y
        };
}
