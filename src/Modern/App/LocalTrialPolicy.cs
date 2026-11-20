using System.Diagnostics.CodeAnalysis;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

internal static class LocalTrialPolicy
{
	public static bool CanActuate([NotNullWhen(true)] GameMemoryProfile? profile)
	{
		if ((object)profile != null && profile.ActuationValidated)
		{
			return true;
		}
		return false;
	}
}
