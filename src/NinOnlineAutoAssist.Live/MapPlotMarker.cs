namespace NinOnlineAutoAssist.Live;

/// <summary>Presentation-only marker classification; row selection never changes the in-game target.</summary>
public enum MapPlotMarker { Player, InGameTarget, TableSelection, Both }

public static class MapPlotMarkers
{
    public static MapPlotMarker Classify(int slot, bool targetedInGame, int? selectedTableSlot) =>
        (selectedTableSlot == slot, targetedInGame) switch
        {
            (true, true) => MapPlotMarker.Both,
            (true, false) => MapPlotMarker.TableSelection,
            (false, true) => MapPlotMarker.InGameTarget,
            _ => MapPlotMarker.Player
        };
}
