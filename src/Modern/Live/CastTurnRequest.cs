using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Live;

public sealed record CastTurnRequest(int Map, string PlayerId, string TargetId, int SpellId, long StartedAtGameMs, long TotalMs, Direction Direction);
