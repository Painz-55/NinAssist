using System;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

/// <summary>
/// Picks the closest living player from the last read map players snapshot.
/// The result is only a candidate; selection is confirmed by a fresh read.
/// </summary>
public static class NearestTargetPolicy
{
	public static TargetInfo? Resolve(LiveFrame? frame, double radiusTiles, bool ignoreSameVillage)
		=> Resolve(frame, radiusTiles, ignoreSameVillage, out _);

	public static TargetInfo? Resolve(LiveFrame? frame, double radiusTiles, bool ignoreSameVillage, out string reason)
	{
		reason = "leitura indisponível";
		MapPlayersFrame? players = frame?.MapPlayers;
		if (frame is null || players is null || players.Players.Count == 0)
		{
			return null;
		}
		if (players.Map != frame.Map || players.LocalId != frame.Snapshot.Player.Id ||
			frame.CapturedAtMs < players.CapturedAtMs || frame.CapturedAtMs - players.CapturedAtMs > 600)
		{
			reason = "lista de jogadores de outro mapa/personagem ou desatualizada";
			return null;
		}
		if (players.UnreadableSlots > 0)
		{
			reason = $"lista incompleta ({players.UnreadableSlots} slots ilegíveis)";
			return null;
		}
		if (!double.IsFinite(radiusTiles) || radiusTiles <= 0)
		{
			reason = "raio inválido";
			return null;
		}
		PlayerState local = frame.Snapshot.Player;
		if (!local.IsAlive)
		{
			reason = "personagem local sem vida confirmada";
			return null;
		}
		double radius = Math.Max(0.5, radiusTiles);
		PlayerVillage own = players.LocalVillage;
		MapPlayer? best = null;
		double bestDistance = double.MaxValue;
		int unconfirmedLife = 0, missingPosition = 0, villageExcluded = 0, outOfRange = 0;
		foreach (MapPlayer player in players.Players)
		{
			if (player.Map != frame.Map || player.IsLocal) continue;
			if (!player.IsAlive) { unconfirmedLife++; continue; }
			if (!player.X.HasValue || !player.Y.HasValue) { missingPosition++; continue; }
			if (ignoreSameVillage && (!PlayerVillages.IsKnown(own) || !PlayerVillages.IsKnown(player.Village) || player.Village == own))
			{
				villageExcluded++;
				continue;
			}
			double dx = player.X.Value - local.PositionX;
			double dy = player.Y.Value - local.PositionY;
			double distance = Math.Sqrt(dx * dx + dy * dy);
			if (!double.IsFinite(distance) || distance > radius) { outOfRange++; continue; }
			if (distance >= bestDistance)
			{
				continue;
			}
			bestDistance = distance;
			best = player;
		}
		if (best is null)
		{
			reason = $"sem candidato: vida não confirmada {unconfirmedLife}, posição ausente {missingPosition}, vila excluída {villageExcluded}, fora do raio {outOfRange}";
			return null;
		}
		reason = $"candidato {best.Slot}:{best.Name} a {bestDistance:0.0} tiles";
		return new TargetInfo
		{
			Id = $"{best.Slot}:{best.Name}",
			PositionX = best.X!.Value,
			PositionY = best.Y!.Value,
			IsAlive = true,
			IsTargeted = false
		};
	}
}
