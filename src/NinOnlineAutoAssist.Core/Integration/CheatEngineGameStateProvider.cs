using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Core.Integration;

/// <summary>
/// Fallback for environments without a configured live source. It never fabricates data.
/// The fingerprinted passive reader is implemented separately in NinOnlineAutoAssist.Live;
/// this stub does not claim that every client/environment can be read successfully.
/// </summary>
public sealed class UnavailableGameStateProvider : IGameStateProvider
{
    public const string Reason =
        "UNAVAILABLE: no authorized data source configured. " +
        "Nin Online has no public API for coordinates/buffs/cooldowns; " +
        "configure a verified live source before enabling modules.";

    public string Status => Reason;
    public bool IsLiveData => false;

    public GameSnapshot? GetSnapshot() => null;
}
