using System;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Logging;

namespace NinOnlineAutoAssist.Live;

public static class LivePunishFactory
{
	public static string Name(LivePunishMode mode)
	{
		if (mode == LivePunishMode.Legacy)
		{
			return "SubPunish Legado";
		}
		throw new ArgumentException("Modo de SubPunish desconhecido");
	}

	public static ILivePunishEngine Create(LivePunishMode mode, IActionExecutor executor, IMonotonicClock clock, EarlyPunishOptions options, IAutoAssistLogger? logger = null)
	{
		if (mode == LivePunishMode.Legacy)
		{
			return new EarlyBuffPunishEngine(executor, clock, options, logger);
		}
		throw new ArgumentException("Modo de SubPunish desconhecido; nenhum motor autorizado");
	}
}
