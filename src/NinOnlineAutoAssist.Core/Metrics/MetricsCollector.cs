using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Core.Metrics;

/// <summary>
/// Collects timing + quality metrics. All intervals use the injected monotonic clock.
/// "Executed" events are counted; "confirmed hits" are only counted when an
/// ActionConfirmed event is observed — they are reported separately and never merged.
/// </summary>
public sealed class MetricsCollector
{
    private readonly IMonotonicClock _clock;

    private long _threatAt = -1;
    private long _activationRequestedAt = -1;
    private long _buffConfirmedAt = -1;
    private long _subTriggeredAt = -1;
    private long _repositionAt = -1;
    private long _firstActionAt = -1;

    public long ThreatsDetected { get; private set; }
    public long ActivationsRequested { get; private set; }
    public long BuffsConfirmed { get; private set; }
    public long AttacksLandedBeforeActivation { get; private set; }
    public long SubsConfirmed { get; private set; }
    public long AlignmentsConfirmed { get; private set; }
    public long CounterAttacksStarted { get; private set; }
    public long SequencesCompleted { get; private set; }
    public long SequencesCancelled { get; private set; }
    public long FalsePositives { get; private set; }   // Sub requested/prepared but no valid threat materialized, or confirmed sub not attributable to a threat
    public long TruePositives { get; private set; }
    public long ActionsExecuted { get; private set; }
    public long ActionsConfirmed { get; private set; }

    // Latest durations (ms), -1 when not yet measured this run.
    public long LastDetectionToActivationMs { get; private set; } = -1;
    public long LastActivationToConfirmMs { get; private set; } = -1;
    public long LastConfirmToRepositionMs { get; private set; } = -1;
    public long LastRepositionToFirstActionMs { get; private set; } = -1;

    public bool FirstActionRecorded { get; private set; }

    public MetricsCollector(IMonotonicClock clock) => _clock = clock;

    public void RecordThreat() { ThreatsDetected++; _threatAt = _clock.NowMs; }
    public void RecordActivationRequested() { ActivationsRequested++; _activationRequestedAt = _clock.NowMs; LastDetectionToActivationMs = _threatAt >= 0 ? _clock.NowMs - _threatAt : -1; }
    public void RecordBuffConfirmed() { BuffsConfirmed++; _buffConfirmedAt = _clock.NowMs; LastActivationToConfirmMs = _activationRequestedAt >= 0 ? _clock.NowMs - _activationRequestedAt : -1; }
    public void RecordAttackBeforeActivation() => AttacksLandedBeforeActivation++;

    public void RecordSubConfirmed()
    {
        SubsConfirmed++; TruePositives++;
        _subTriggeredAt = _clock.NowMs;
        _repositionAt = _clock.NowMs;
        LastConfirmToRepositionMs = _buffConfirmedAt >= 0 ? _clock.NowMs - _buffConfirmedAt : -1;
    }

    public void RecordAlignmentConfirmed() => AlignmentsConfirmed++;
    public void RecordCounterAttackStarted() => CounterAttacksStarted++;

    public void RecordFirstAction()
    {
        FirstActionRecorded = true;
        _firstActionAt = _clock.NowMs;
        LastRepositionToFirstActionMs = _repositionAt >= 0 ? _clock.NowMs - _repositionAt : -1;
        ActionsExecuted++;
    }

    public void RecordActionExecuted() => ActionsExecuted++;
    public void RecordActionConfirmed() => ActionsConfirmed++;

    public void RecordSequenceCompleted() { SequencesCompleted++; FirstActionRecorded = false; }
    public void RecordSequenceCancelled(string reason)
    {
        SequencesCancelled++;
        FirstActionRecorded = false;
        // A "teleport not confirmed" cancellation after activation counts as a false positive candidate.
        if (reason.Contains("Teleport not confirmed", StringComparison.OrdinalIgnoreCase))
            FalsePositives++;
    }

    public double FalsePositiveRate =>
        FalsePositives + TruePositives == 0 ? 0.0 : (double)FalsePositives / (FalsePositives + TruePositives);

    public double CompletionRate =>
        SequencesCompleted + SequencesCancelled == 0 ? 0.0 : (double)SequencesCompleted / (SequencesCompleted + SequencesCancelled);

    public string Summary() =>
        $"threats={ThreatsDetected} actReq={ActivationsRequested} buffOk={BuffsConfirmed} " +
        $"hitBeforeAct={AttacksLandedBeforeActivation} subsOk={SubsConfirmed} alignOk={AlignmentsConfirmed} " +
        $"started={CounterAttacksStarted} completed={SequencesCompleted} cancelled={SequencesCancelled} " +
        $"FP-rate={FalsePositiveRate:P0} completion={CompletionRate:P0} " +
        $"t(detect→act)={LastDetectionToActivationMs}ms t(act→confirm)={LastActivationToConfirmMs}ms " +
        $"t(confirm→repos)={LastConfirmToRepositionMs}ms t(repos→1stAction)={LastRepositionToFirstActionMs}ms " +
        $"actionsExec={ActionsExecuted} actionsConfirmed={ActionsConfirmed}";
}
