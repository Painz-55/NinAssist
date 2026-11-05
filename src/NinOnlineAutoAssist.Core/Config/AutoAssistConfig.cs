using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.SubPunish;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NinOnlineAutoAssist.Core.Config;

[JsonConverter(typeof(JsonStringEnumConverter))]
// AnchoredExperimental is retained solely to read old per-character JSON safely.
// The active factory rejects it; ConfigLoader migrates it to Legacy with consent OFF.
public enum LivePunishMode { Legacy, AnchoredExperimental }

/// <summary>
/// Root configuration, deserialized from ninautoassist.config.json.
/// Values that have NOT been validated in-game are marked with "experimental": true
/// in their section's <c>ExperimentalParameters</c> list and documented as such.
/// </summary>
public sealed record AutoAssistConfig
{
    public AutoTurningSection AutoTurning { get; init; } = new();
    public SubPunishSection SubPunish { get; init; } = new();
    public GeneralSection General { get; init; } = new();
    public LiveSection Live { get; init; } = new();
    public CombatAssistSettings CombatAssist { get; init; } = new();
    public AutoCastTurnSettings AutoCastTurn { get; init; } = new();

    public sealed record LiveSection
    {
        public LivePunishMode PunishMode { get; init; } = LivePunishMode.Legacy;
        public string CharacterName { get; init; } = "";
        public bool AllowCounterAttackSequence { get; init; } = false;
        public long EarlyExpiryMarginMs { get; init; } = 1000;
        public int EarlyExpiryAbsentSamples { get; init; } = 2;
        public long PunishKeyIntervalMs { get; init; } = 120;
        public string[] PunishKeys { get; init; } = new[] { "A", "Q", "S" };
        public string[] TurnActivationKeys { get; init; } = new[] { "RightCtrl" };
        public long PunishFirstToSecondMs { get; init; } = 0;
        public long PunishTriggerDelayMs { get; init; } = 0;
        public long PunishSecondToThirdMs { get; init; } = 200;
        public long PunishSequenceTimeoutMs { get; init; } = 1500;
    }

    public sealed record AutoTurningSection
    {
        public bool Enabled { get; init; } = false;
        public long UpdateIntervalMs { get; init; } = 150;
        /// <summary>Positional tolerance in game units. EXPERIMENTAL — unit scale for Nin Online is unverified.</summary>
        public double PositionTolerance { get; init; } = 4.0;
        public double HysteresisRatio { get; init; } = 1.25;
        public bool ManualMovementPriority { get; init; } = true;
        public bool SuspendDuringCasting { get; init; } = true;
        public long ManualOverrideDurationMs { get; init; } = 800;
        public long MinCommandIntervalMs { get; init; } = 180;
        public bool DiagnosticMode { get; init; } = false;
        /// <summary>Names of AutoTurning parameters not validated in-game.</summary>
        public List<string> ExperimentalParameters { get; init; } = new() { "PositionTolerance", "HysteresisRatio" };
        /// <summary>Configurable turn keys — Nin Online has dedicated turn keys; NEVER assumed.</summary>
        public Dictionary<string, string> TurnKeys { get; init; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Up"] = "Home", ["Down"] = "End", ["Left"] = "Delete", ["Right"] = "PageDown"
        };
    }

    public sealed record SubPunishSection
    {
        public bool Enabled { get; init; } = false;
        public bool AutoPrepareSub { get; init; } = true;
        public string SubstitutionKey { get; init; } = "F";
        /// <summary>EXPERIMENTAL. Documented ~7s for base Substitution; game updates may change it.</summary>
        public double SubstitutionDurationSec { get; init; } = 7.0;
        /// <summary>EXPERIMENTAL. Documented ~16/15/14s for levels 1/2/3. Configure per your level.</summary>
        public double SubstitutionCooldownSec { get; init; } = 16.0;
        public long TeleportConfirmationTimeoutMs { get; init; } = 1500;
        public long CounterAttackTimeoutMs { get; init; } = 2500;
        /// <summary>EXPERIMENTAL. There is NO proven guaranteed post-teleport window; measure in tests. Not the 7s buff duration.</summary>
        public long CounterAttackWindowMs { get; init; } = 1500;
        public long OrientationTimeoutMs { get; init; } = 1200;
        public long InterActionIntervalMs { get; init; } = 120;
        public long GlobalCooldownMs { get; init; } = 300;   // EXPERIMENTAL
        public long EstimatedLatencyMs { get; init; } = 80;  // EXPERIMENTAL
        public long ThreatExpiryMs { get; init; } = 3000;
        public long ActivationConfirmTimeoutMs { get; init; } = 1200;
        public int MinEvidenceScore { get; init; } = 4;
        public double MinConfidence { get; init; } = 0.6;
        public double MaxThreatDistance { get; init; } = 6.0;
        public double TeleportMinDistance { get; init; } = 1.5;
        public double TeleportMaxDistance { get; init; } = 30.0;
        public string TargetSelectionPolicy { get; init; } = "CancelOnMismatch"; // or ProceedWithAttacker
        public List<CounterAttackAction> AttackSequence { get; init; } = new()
        {
            new() { Id = "basic_attack", Key = "Space", MaxRange = 4.0, CastTimeMs = 0, ResourceCost = 0, CooldownMs = 0 },
            new() { Id = "jutsu_slot_1", Key = "1", MaxRange = 6.0, CastTimeMs = 400, ResourceCost = 10, CooldownMs = 4000 },
            new() { Id = "jutsu_slot_2", Key = "2", MaxRange = 8.0, CastTimeMs = 800, ResourceCost = 25, CooldownMs = 8000 },
        };
        /// <summary>Names of parameters not yet validated in-game. Surfaced in the UI/docs.</summary>
        public List<string> ExperimentalParameters { get; init; } = new()
        {
            "SubstitutionDurationSec", "SubstitutionCooldownSec", "CounterAttackWindowMs",
            "GlobalCooldownMs", "EstimatedLatencyMs", "AttackSequence[*]", "TeleportMinDistance"
        };
    }

    public sealed record GeneralSection
    {
        public bool Logging { get; init; } = true;
        public bool DebugMode { get; init; } = false;
        public string EmergencyStopKey { get; init; } = "F12";
        public string LogFile { get; init; } = "logs/autoassist.log";
    }

    // ── mapping helpers ──────────────────────────────────────────────

    public AutoTurningOptions ToAutoTurningOptions() => new()
    {
        Enabled = AutoTurning.Enabled,
        MinCommandIntervalMs = AutoTurning.MinCommandIntervalMs,
        SuspendDuringCasting = AutoTurning.SuspendDuringCasting,
        ManualMovementPriority = AutoTurning.ManualMovementPriority,
        ManualOverrideDurationMs = AutoTurning.ManualOverrideDurationMs,
        DiagnosticMode = AutoTurning.DiagnosticMode,
        Geometry = new DirectionCalculatorOptions
        {
            PositionTolerance = AutoTurning.PositionTolerance,
            HysteresisRatio = AutoTurning.HysteresisRatio
        }
    };

    public AttackDetectionOptions ToDetectionOptions() => new()
    {
        MinConfidence = SubPunish.MinConfidence,
        MaxThreatDistance = SubPunish.MaxThreatDistance
    };

    public SubTriggerVerificationOptions ToVerificationOptions() => new()
    {
        TeleportConfirmationTimeoutMs = SubPunish.TeleportConfirmationTimeoutMs,
        MinEvidenceScore = SubPunish.MinEvidenceScore,
        TeleportMinDistance = SubPunish.TeleportMinDistance,
        TeleportMaxDistance = SubPunish.TeleportMaxDistance
    };

    public SubstitutionOptions ToSubstitutionOptions() => new()
    {
        SubstitutionKey = SubPunish.SubstitutionKey,
        SubstitutionDurationSec = SubPunish.SubstitutionDurationSec,
        SubstitutionCooldownSec = SubPunish.SubstitutionCooldownSec,
        ActivationConfirmTimeoutMs = SubPunish.ActivationConfirmTimeoutMs
    };

    public CounterAttackOptions ToCounterAttackOptions() => new()
    {
        AttackWindowMs = SubPunish.CounterAttackWindowMs,
        CounterAttackTimeoutMs = SubPunish.CounterAttackTimeoutMs,
        InterActionIntervalMs = SubPunish.InterActionIntervalMs,
        GlobalCooldownMs = SubPunish.GlobalCooldownMs,
        EstimatedLatencyMs = SubPunish.EstimatedLatencyMs,
        AttackSequence = SubPunish.AttackSequence
    };

    public SubPunishOptions ToSubPunishOptions() => new()
    {
        Enabled = SubPunish.Enabled,
        AutoPrepareSub = SubPunish.AutoPrepareSub,
        TargetSelectionPolicy = Enum.TryParse<TargetSelectionPolicy>(SubPunish.TargetSelectionPolicy, true, out var p)
            ? p : TargetSelectionPolicy.CancelOnMismatch,
        OrientationTimeoutMs = SubPunish.OrientationTimeoutMs,
        ThreatExpiryMs = SubPunish.ThreatExpiryMs
    };
}

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AutoAssistConfig Load(string path)
    {
        if (!File.Exists(path))
            return new AutoAssistConfig(); // safe defaults: modules and offensive input disabled
        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<AutoAssistConfig>(json, Json) ?? new AutoAssistConfig();
        return loaded.Live.PunishMode == LivePunishMode.AnchoredExperimental
            ? loaded with { Live = loaded.Live with { PunishMode = LivePunishMode.Legacy, AllowCounterAttackSequence = false } }
            : loaded;
    }

    public static void SaveDefault(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(new AutoAssistConfig(), Json));
    }

    public static void Save(string path, AutoAssistConfig config) =>
        File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
}
