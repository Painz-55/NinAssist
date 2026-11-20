using System.Collections.Generic;

namespace NinOnlineAutoAssist.Live;

internal sealed record RemovalSample(IReadOnlyList<EarlyRemoval> Ready, int PendingSamples, bool Watching, bool KnownDuration, bool Expired, bool NewCycle);
