using System;
using System.Collections.Generic;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed record LiveFrame(GameSnapshot Snapshot, long CapturedAtMs, int Map, string PlayerName, string? TargetName, bool SubstitutionActive, int? SubstitutionDuration, long ReadDurationMs)
{
	public StaminaObservation? Stamina { get; init; }

	public CastObservation? Cast { get; init; }

	public IReadOnlyList<ObservedBuff> ObservedActiveBuffs { get; init; } = Array.Empty<ObservedBuff>();

	public IReadOnlyList<string> UnmappedSignals { get; init; } = new string[6] { "Casting", "ActionBlock", "SubstitutionCooldownRemaining", "Threat", "AttackerIdentity", "QSCostCooldownAndEffect" };

	/// <summary>Last known map players snapshot attached by the reader, when available.</summary>
	public MapPlayersFrame? MapPlayers { get; init; }
}
