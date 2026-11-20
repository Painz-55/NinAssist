using System;

namespace NinOnlineAutoAssist.Live;

public static class OverlayProjection
{
	private const double PixelsPerTile = 32.0;

	public static RenderViewObservation? CalibratedMap190(double localX, double localY, int gridWidth, int gridHeight, double sizeX, double sizeY)
	{
		bool flag = gridWidth != 101 || gridHeight != 101 || !double.IsFinite(localX) || !double.IsFinite(localY) || localX < 0.0 || localX >= (double)gridWidth || localY < 0.0 || localY >= (double)gridHeight || !double.IsFinite(sizeX) || !double.IsFinite(sizeY);
		if (!flag)
		{
			bool flag2 = ((sizeX < 320.0 || sizeX > 8000.0) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			bool flag2 = ((sizeY < 240.0 || sizeY > 8000.0) ? true : false);
			flag3 = flag2;
		}
		if (flag3 || sizeX > (double)gridWidth * 32.0 || sizeY > (double)gridHeight * 32.0)
		{
			return null;
		}
		return new RenderViewObservation(Math.Clamp(localX * 32.0, sizeX / 2.0, (double)gridWidth * 32.0 - sizeX / 2.0), Math.Clamp(localY * 32.0, sizeY / 2.0, (double)gridHeight * 32.0 - sizeY / 2.0), sizeX, sizeY);
	}

	public static (double X, double Y)? Project(NearbyPlayerMarkers.Marker marker, LiveFrame? frame, MapPlayersFrame? map, double clientWidth, double clientHeight)
	{
		RenderViewObservation renderView = default(RenderViewObservation);
		int num;
		if ((object)frame != null && (object)map != null && map.Map == frame.Map && !(map.LocalId != frame.Snapshot.Player.Id))
		{
			renderView = map.RenderView;
			if ((object)renderView != null && double.IsFinite(renderView.CenterX) && double.IsFinite(renderView.CenterY) && double.IsFinite(renderView.SizeX))
			{
				num = ((!double.IsFinite(renderView.SizeY)) ? 1 : 0);
				goto IL_0073;
			}
		}
		num = 1;
		goto IL_0073;
		IL_0119:
		bool flag2;
		bool flag = flag2;
		bool flag3;
		if (!flag)
		{
			int? gridHeight = map.GridHeight;
			if (gridHeight.HasValue)
			{
				int valueOrDefault = gridHeight.GetValueOrDefault();
				if (valueOrDefault > 0 && valueOrDefault <= 512)
				{
					flag3 = false;
					goto IL_0151;
				}
			}
			flag3 = true;
			goto IL_0151;
		}
		goto IL_0155;
		IL_0155:
		bool flag4 = flag || !double.IsFinite(clientWidth) || !double.IsFinite(clientHeight);
		if (!flag4)
		{
			flag3 = ((clientWidth < 320.0 || clientWidth > 8000.0) ? true : false);
			flag4 = flag3;
		}
		bool flag5 = flag4;
		if (!flag5)
		{
			flag3 = ((clientHeight < 240.0 || clientHeight > 8000.0) ? true : false);
			flag5 = flag3;
		}
		if (flag5)
		{
			return null;
		}
		double positionX = frame.Snapshot.Player.PositionX;
		double positionY = frame.Snapshot.Player.PositionY;
		double num2 = positionX + marker.OffsetX;
		double num3 = positionY + marker.OffsetY;
		if (!double.IsFinite(num2) || !double.IsFinite(num3) || positionX < 0.0 || positionY < 0.0 || num2 < 0.0 || num3 < 0.0 || num2 >= (double?)map.GridWidth || num3 >= (double?)map.GridHeight)
		{
			return null;
		}
		double num4 = (num2 * 32.0 - renderView.CenterX + renderView.SizeX / 2.0) * clientWidth / renderView.SizeX;
		double num5 = (num3 * 32.0 - renderView.CenterY + renderView.SizeY / 2.0) * clientHeight / renderView.SizeY;
		if (!(num4 >= 0.0) || !(num5 >= 0.0) || !(num4 < clientWidth) || !(num5 < clientHeight))
		{
			return null;
		}
		return (num4, num5);
		IL_0151:
		flag = flag3;
		goto IL_0155;
		IL_0073:
		bool flag6 = (byte)num != 0;
		if (!flag6)
		{
			double sizeX = renderView.SizeX;
			flag3 = ((sizeX < 320.0 || sizeX > 8000.0) ? true : false);
			flag6 = flag3;
		}
		bool flag7 = flag6;
		if (!flag7)
		{
			double sizeX = renderView.SizeY;
			flag3 = ((sizeX < 240.0 || sizeX > 8000.0) ? true : false);
			flag7 = flag3;
		}
		flag2 = flag7;
		if (!flag2)
		{
			int? gridHeight = map.GridWidth;
			if (gridHeight.HasValue)
			{
				int valueOrDefault = gridHeight.GetValueOrDefault();
				if (valueOrDefault > 0 && valueOrDefault <= 512)
				{
					flag3 = false;
					goto IL_0115;
				}
			}
			flag3 = true;
			goto IL_0115;
		}
		goto IL_0119;
		IL_0115:
		flag2 = flag3;
		goto IL_0119;
	}
}
