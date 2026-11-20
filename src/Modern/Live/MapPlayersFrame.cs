using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public sealed record MapPlayersFrame(int Map, string LocalId, long CapturedAtMs, IReadOnlyList<MapPlayer> Players, int UnreadableSlots)
{
	public int? GridWidth { get; init; }

	public int? GridHeight { get; init; }

	public RenderViewObservation? RenderView { get; init; }

	public PlayerVillage LocalVillage
	{
		get
		{
			MapPlayer[] array = Players.Where((MapPlayer p) => p.IsLocal).Take(2).ToArray();
			if (array.Length != 1 || !PlayerVillages.IsKnown(array[0].Village))
			{
				return PlayerVillage.Unknown;
			}
			return array[0].Village;
		}
	}

	public IReadOnlyList<MapPlayer> ForDisplay(bool hideOwnVillage)
	{
		if (!hideOwnVillage)
		{
			return Players;
		}
		PlayerVillage own = LocalVillage;
		if (!PlayerVillages.IsKnown(own))
		{
			return Array.Empty<MapPlayer>();
		}
		return Array.AsReadOnly(Players.Where((MapPlayer p) => !p.IsLocal && PlayerVillages.IsKnown(p.Village) && p.Village != own).ToArray());
	}

	public IReadOnlyList<MapPlayer> ForDisplay(bool hideOwnVillage, IReadOnlyCollection<PlayerVillage> hiddenVillages)
	{
		ArgumentNullException.ThrowIfNull(hiddenVillages, "hiddenVillages");
		if (!hideOwnVillage && hiddenVillages.Count == 0)
		{
			return Players;
		}
		PlayerVillage own = LocalVillage;
		return Array.AsReadOnly(Players.Where((MapPlayer p) => VillageVisible(p.Village, p.IsLocal, own, hideOwnVillage, hiddenVillages)).ToArray());
	}

	public static bool VillageVisible(PlayerVillage village, bool isLocal, PlayerVillage own, bool hideOwnVillage, IReadOnlyCollection<PlayerVillage> hiddenVillages)
	{
		if (!hideOwnVillage || (PlayerVillages.IsKnown(own) && !isLocal && PlayerVillages.IsKnown(village) && village != own))
		{
			if (PlayerVillages.IsKnown(village))
			{
				return !hiddenVillages.Contains(village);
			}
			return true;
		}
		return false;
	}

	public IReadOnlyList<NumberedMapPlayer> NumberedForDisplay(bool hideOwnVillage, IReadOnlyCollection<PlayerVillage> hiddenVillages)
	{
		return Array.AsReadOnly(ForDisplay(hideOwnVillage, hiddenVillages).Select((MapPlayer player, int index) => new NumberedMapPlayer(index + 1, player)).ToArray());
	}

	public bool IsCurrent(LiveFrame? live, long nowMs)
	{
		if ((object)live != null && live.Map == Map && live.Snapshot.Player.Id == LocalId && nowMs >= CapturedAtMs && nowMs - CapturedAtMs <= 1500 && nowMs >= live.CapturedAtMs)
		{
			return nowMs - live.CapturedAtMs <= 500;
		}
		return false;
	}

	public static IReadOnlyList<MapPlayer> Filter(IEnumerable<MapPlayer> records, int map)
	{
		return Array.AsReadOnly((from p in records
			where p.Map == map && p.Slot > 0 && !string.IsNullOrWhiteSpace(p.Name)
			orderby p.IsLocal descending
			select p).ThenBy<MapPlayer, string>((MapPlayer p) => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy((MapPlayer p) => p.Slot).ToArray());
	}
}
