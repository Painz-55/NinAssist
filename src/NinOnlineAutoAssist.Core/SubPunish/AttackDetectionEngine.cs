using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.SubPunish;

public sealed record AttackDetectionOptions
{
    /// <summary>Minimum reported confidence (0..1) to consider the attack a real threat.</summary>
    public double MinConfidence { get; init; } = 0.6;
    /// <summary>Maximum distance (units) at which the attack can threaten the player.</summary>
    public double MaxThreatDistance { get; init; } = 6.0;
    /// <summary>Animation states that indicate an attack is committed (e.g. "attack_cast", "attack_release"). Configurable because the game changes.</summary>
    public IReadOnlySet<string> CommittedAnimationStates { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "attack_cast", "attack_release", "jutsu_release" };
    /// <summary>Attack types that are never a substitution trigger (e.g. buffs, heals).</summary>
    public IReadOnlySet<string> IgnoredAttackTypes { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "buff", "heal", "emote" };
}

public sealed record ThreatAssessment(bool IsValidThreat, string Reason, CombatEvent Source);

/// <summary>
/// Evaluates whether a detected attack animation constitutes a real threat worth
/// preparing a substitution for. Detection NEVER implies the hit will land —
/// the hit must still be observed (AttackLanded) or the sub must trigger.
/// </summary>
public sealed class AttackDetectionEngine
{
    private readonly IMonotonicClock _clock;
    private readonly IAutoAssistLogger _log;
    private AttackDetectionOptions _options;

    public AttackDetectionEngine(IMonotonicClock clock, IAutoAssistLogger? logger = null,
        AttackDetectionOptions? options = null)
    {
        _clock = clock;
        _log = logger ?? NullLogger.Instance;
        _options = options ?? new AttackDetectionOptions();
    }

    public AttackDetectionOptions Options => _options;
    public void UpdateOptions(AttackDetectionOptions options) => _options = options;

    /// <summary>
    /// Assess a raw threat event against current snapshot. Returns validity + reason.
    /// </summary>
    public ThreatAssessment Assess(CombatEvent threat, GameSnapshot snapshot)
    {
        if (threat.Kind != CombatEventKind.ThreatDetected)
            return new(false, "Not a ThreatDetected event", threat);

        if (string.IsNullOrWhiteSpace(threat.AttackerId))
            return new(false, "Missing attacker id", threat);

        if (threat.TargetId is not null &&
            !string.Equals(threat.TargetId, snapshot.Player.Id, StringComparison.Ordinal))
            return new(false, "Attack targets someone else", threat);

        if (threat.Confidence is { } c && c < _options.MinConfidence)
            return new(false, $"Confidence {c:F2} below minimum {_options.MinConfidence:F2}", threat);

        if (threat.Distance is { } d && d > _options.MaxThreatDistance)
            return new(false, $"Distance {d:F1} beyond max threat range {_options.MaxThreatDistance:F1}", threat);

        if (threat.AttackType is { } t && _options.IgnoredAttackTypes.Contains(t))
            return new(false, $"Attack type '{t}' is ignored", threat);

        if (threat.AnimationState is { } anim &&
            _options.CommittedAnimationStates.Count > 0 &&
            !_options.CommittedAnimationStates.Contains(anim))
            return new(false, $"Animation state '{anim}' is not a committed attack", threat);

        _log.Log(LogLevel.Info, $"Threat validated: attacker={threat.AttackerId} dist={threat.Distance?.ToString("F1") ?? "?"} conf={threat.Confidence?.ToString("F2") ?? "?"}", threat.Origin);
        return new(true, "Valid threat", threat);
    }
}
