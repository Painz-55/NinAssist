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
	{
		MapPlayersFrame? players = frame?.MapPlayers;
		if (frame is null || players is null || players.Players.Count == 0 ||
			players.Map != frame.Map || players.LocalId != frame.Snapshot.Player.Id ||
			frame.CapturedAtMs < players.CapturedAtMs || frame.CapturedAtMs - players.CapturedAtMs > 600 ||
			players.UnreadableSlots > 0 || !double.IsFinite(radiusTiles) || radiusTiles <= 0)
		{
			return null;
		}
		PlayerState local = frame.Snapshot.Player;
		if (!local.IsAlive)
		{
			return null;
		}
		double radius = Math.Max(0.5, radiusTiles);
		PlayerVillage own = players.LocalVillage;
		MapPlayer? best = null;
		double bestDistance = double.MaxValue;
		foreach (MapPlayer player in players.Players)
		{
			if (player.Map != frame.Map || player.IsLocal || !player.IsAlive || !player.X.HasValue || !player.Y.HasValue)
			{
				continue;
			}
			// Unknown village cannot prove that the candidate is not an ally.
			if (ignoreSameVillage && (!PlayerVillages.IsKnown(own) || !PlayerVillages.IsKnown(player.Village) || player.Village == own))
			{
				continue;
			}
			double dx = player.X.Value - local.PositionX;
			double dy = player.Y.Value - local.PositionY;
			double distance = Math.Sqrt(dx * dx + dy * dy);
			if (!double.IsFinite(distance) || distance > radius || distance >= bestDistance)
			{
				continue;
			}
			bestDistance = distance;
			best = player;
		}
		if (best is null)
		{
			return null;
		}
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
