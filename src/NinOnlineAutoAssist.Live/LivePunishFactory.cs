using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Logging;

namespace NinOnlineAutoAssist.Live;

public static class LivePunishFactory
{
    public static string Name(LivePunishMode mode) => mode switch
    {
        LivePunishMode.Legacy => "SubPunish Legado",
        LivePunishMode.AnchoredExperimental => "SubPunish Novo (experimental)",
        _ => throw new ArgumentException("Modo de SubPunish desconhecido")
    };
    public static ILivePunishEngine Create(LivePunishMode mode, IActionExecutor executor, IMonotonicClock clock,
        EarlyPunishOptions options, IAutoAssistLogger? logger = null) => mode switch
    {
        LivePunishMode.Legacy => new EarlyBuffPunishEngine(executor, clock, options, logger),
        LivePunishMode.AnchoredExperimental => new AnchoredSubPunishEngine(executor, clock, options, logger),
        _ => throw new ArgumentException("Modo de SubPunish desconhecido; nenhum motor autorizado")
    };
}
