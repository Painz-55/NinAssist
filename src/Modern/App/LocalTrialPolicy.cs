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
		// Only signed, exact-build local trials can actuate while unapproved.
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
		if (profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false,
			Sha256: "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525",
			CommonSha256: "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102",
			PunishComboValidated: false } steam &&
			steam.ValidatedAttackModes.Length == 0 &&
			(steam.Id == "LOCAL TRIAL Steam Raizen Turning Stamina 2026-11-26" &&
			 steam.ValidatedInputKinds.SequenceEqual(["Turning", "Stamina"]) ||
			 steam.Id == "LOCAL TRIAL Steam Raizen TargetMemory 2026-11-26" &&
			 steam.ValidatedInputKinds.SequenceEqual(["TargetMemory"])))
			return true;
		if (profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false,
			Sha256: "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525",
			CommonSha256: "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102",
			Id: "LOCAL TRIAL Steam Integrated Raizen 2026-11-26",
			PunishComboValidated: true, PunishComboRequiresTarget: true } integratedSteam &&
			integratedSteam.ValidatedInputKinds.SequenceEqual(["Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory"]) &&
			integratedSteam.ValidatedAttackModes.SequenceEqual(["Melee", "Fan"]))
			return true;
		if (profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false,
			Sha256: "57668CBF395C8A503ABA85C8D7181D5712701F7C88ADF5B6EA49EF54DCEEEC89",
			CommonSha256: "4DB42166C9D1919083E5B03757B9E8D166145E9C39A410AC22415F1D71171DEB",
			Id: "LOCAL TRIAL Steam Raizen 57668CBF 2026-11-26", PunishComboValidated: false } currentSteam &&
			currentSteam.ValidatedInputKinds.SequenceEqual(["Turning", "Stamina", "TargetMemory"]) &&
			currentSteam.ValidatedAttackModes.Length == 0 &&
			currentSteam.SelectedTarget is { GlobalObject: "Ku0ZF1aWYsJ", Kind: "nJ5VIOJ8p8E", Index: "mpsVI6aspnF", PlayerKind: 1 })
			return true;
		return false;
	}
}
