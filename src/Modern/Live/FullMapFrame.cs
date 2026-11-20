using System.Collections.Generic;

namespace NinOnlineAutoAssist.Live;

public enum MapTerrainCell : byte
{
	Unknown,
	Ground,
	Water,
	Wall,
	Portal,
	Capture,
	Special
}

public sealed record FullMapFrame(int Map, long CapturedAtMs, int Width, int Height, IReadOnlyList<MapTerrainCell> Cells)
{
	public MapTerrainCell Cell(int x, int y)
	{
		return x >= 0 && y >= 0 && x < Width && y < Height && Cells.Count == Width * Height
			? Cells[y * Width + x]
			: MapTerrainCell.Unknown;
	}
}
