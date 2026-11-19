using NinOnlineAutoAssist.Live;
using System.Diagnostics.CodeAnalysis;

namespace NinOnlineAutoAssist.App;

internal static class LocalTrialPolicy
{
    public static bool CanActuate([NotNullWhen(true)] GameMemoryProfile? profile)
    {
        if (profile?.ActuationValidated == true) return true;
#if LOCAL_ARCHIVE
        return profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false } &&
            profile.Id.StartsWith("LOCAL TRIAL ", StringComparison.Ordinal);
#else
        return false;
#endif
    }
}
