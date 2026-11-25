using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

internal static class LocalTrialPolicy
{
	public static bool CanActuate([NotNullWhen(true)] GameMemoryProfile? profile)
	{
		if (profile is { ActuationValidated: true })
		{
			return true;
		}
		// Only the signed, exact-build local target trial can actuate while unapproved.
		// The production catalog/feed and every other input remain fail-closed.
		if (profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false,
			Sha256: "8A163FC3D8FA13CCB3AA1FBABD9D1EBA1C829FBAE4ED14CC05AC688CAD328D50",
			CommonSha256: "43B99E8E93F063DFA1239978C7B41C51520E765CAB8D21499160FCA14CD01F62" } trial &&
			trial.Id.StartsWith("LOCAL TRIAL ", StringComparison.Ordinal) &&
			(trial.ValidatedInputKinds.SequenceEqual(["TargetMemory"]) &&
			 trial.ValidatedAttackModes.Length == 0 && trial.PunishComboValidated == false ||
			 trial.ValidatedInputKinds.SequenceEqual(["Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory"]) &&
			 trial.ValidatedAttackModes.SequenceEqual(["Melee", "Fan"]) &&
			 trial.PunishComboValidated == true && trial.PunishComboRequiresTarget == true))
			return true;
		return false;
	}
}
