namespace NinOnlineAutoAssist.Live;

public static class MapPlotMarkers
{
	public static MapPlotMarker Classify(int slot, bool targetedInGame, int? selectedTableSlot)
	{
		if (selectedTableSlot == slot)
		{
			if (targetedInGame)
			{
				return MapPlotMarker.Both;
			}
			return MapPlotMarker.TableSelection;
		}
		if (targetedInGame)
		{
			return MapPlotMarker.InGameTarget;
		}
		return MapPlotMarker.Player;
	}
}
