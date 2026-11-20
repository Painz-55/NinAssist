using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

internal sealed class MapPlayersPlot : Control
{
	private float _zoom = 1f;
	private Point? _dragStart;
	private PointF _pan;

	public IReadOnlyList<(int Slot, string Name, double X, double Y, bool Targeted)> Players { get; set; } = Array.Empty<(int, string, double, double, bool)>();
	public FullMapFrame? Terrain { get; set; }
	public int? MapNumber { get; set; }
	public int? GridWidth { get; set; }
	public int? GridHeight { get; set; }
	public double PlayerX { get; set; }
	public double PlayerY { get; set; }
	public string LoadingStatus { get; set; } = "Aguardando leitura do mapa completo…";
	public int? SelectedRowSlot { get; set; }
	public bool SelectedHighlightOn { get; set; }
	public bool HasSelectedRow => SelectedRowSlot is int slot && Players.Any(p => p.Slot == slot);

	public MapPlayersPlot()
	{
		DoubleBuffered = true;
		Dock = DockStyle.Fill;
		BackColor = Color.FromArgb(10, 14, 19);
		Cursor = Cursors.Cross;
		SetStyle(ControlStyles.ResizeRedraw, true);
	}

	public void ResetScale()
	{
		_zoom = 1f;
		_pan = PointF.Empty;
		Players = Array.Empty<(int, string, double, double, bool)>();
		Terrain = null;
		Invalidate();
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		_zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.16f : 1f / 1.16f), .55f, 8f);
		Invalidate();
		base.OnMouseWheel(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (e.Button == MouseButtons.Left) _dragStart = e.Location;
		if (e.Button == MouseButtons.Left && e.Clicks > 1) { _pan = PointF.Empty; _zoom = 1f; Invalidate(); }
		Focus();
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (_dragStart is Point start && e.Button == MouseButtons.Left)
		{
			_pan = new PointF(_pan.X + e.X - start.X, _pan.Y + e.Y - start.Y);
			_dragStart = e.Location;
			Invalidate();
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e) { _dragStart = null; base.OnMouseUp(e); }

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(BackColor);
		if (Width < 100 || Height < 100) return;
		int mapWidth = Terrain?.Width ?? GridWidth ?? 0;
		int mapHeight = Terrain?.Height ?? GridHeight ?? 0;
		if (mapWidth <= 0 || mapHeight <= 0) { DrawEmpty(g, LoadingStatus); return; }
		const float padding = 42f;
		float fit = Math.Min((Width - padding * 2) / mapWidth, (Height - padding * 2) / mapHeight);
		float scale = Math.Max(.35f, fit * _zoom);
		var origin = new PointF((Width - mapWidth * scale) / 2f + _pan.X, (Height - mapHeight * scale) / 2f + _pan.Y);
		DrawTerrain(g, origin, scale, mapWidth, mapHeight);
		if (Terrain == null)
		{
			using var loadingFont = new Font("Segoe UI Semibold", 9.5f);
			TextRenderer.DrawText(g, LoadingStatus, loadingFont, new Rectangle(20, Height - 42, Width - 40, 28),
				Color.FromArgb(255, 205, 92), TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
		}
		DrawPlayers(g, origin, scale);
		DrawHud(g, mapWidth, mapHeight);
	}

	private void DrawTerrain(Graphics g, PointF origin, float scale, int mapWidth, int mapHeight)
	{
		using var ground = new SolidBrush(Color.FromArgb(31, 67, 111));
		using var water = new SolidBrush(Color.FromArgb(24, 55, 96));
		using var wall = new SolidBrush(Color.FromArgb(35, 43, 54));
		using var portal = new SolidBrush(Color.FromArgb(91, 71, 170));
		using var capture = new SolidBrush(Color.FromArgb(190, 145, 48));
		using var special = new SolidBrush(Color.FromArgb(27, 63, 108));
		using var unknown = new SolidBrush(Color.FromArgb(18, 24, 32));
		if (Terrain is { } terrain && terrain.Cells.Count == terrain.Width * terrain.Height)
		{
			for (int y = 0; y < terrain.Height; y++)
			for (int x = 0; x < terrain.Width; x++)
			{
				Brush brush = terrain.Cells[y * terrain.Width + x] switch
				{
					MapTerrainCell.Ground => ground, MapTerrainCell.Water => water,
					MapTerrainCell.Wall => wall, MapTerrainCell.Portal => portal,
					MapTerrainCell.Capture => capture, MapTerrainCell.Special => special, _ => unknown
				};
				g.FillRectangle(brush, origin.X + x * scale, origin.Y + y * scale, Math.Max(1.05f, scale), Math.Max(1.05f, scale));
			}
		}
		else g.FillRectangle(unknown, origin.X, origin.Y, mapWidth * scale, mapHeight * scale);
		using var border = new Pen(Color.FromArgb(95, 121, 141, 166), 1f);
		g.DrawRectangle(border, origin.X, origin.Y, mapWidth * scale, mapHeight * scale);
		if (scale >= 9f)
		{
			using var grid = new Pen(Color.FromArgb(25, 255, 255, 255));
			for (int x = 0; x <= mapWidth; x++) g.DrawLine(grid, origin.X + x * scale, origin.Y, origin.X + x * scale, origin.Y + mapHeight * scale);
			for (int y = 0; y <= mapHeight; y++) g.DrawLine(grid, origin.X, origin.Y + y * scale, origin.X + mapWidth * scale, origin.Y + y * scale);
		}
	}

	private void DrawPlayers(Graphics g, PointF origin, float scale)
	{
		foreach (var item in Players.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
		{
			float x = origin.X + ((float)item.X + .5f) * scale;
			float y = origin.Y + ((float)item.Y + .5f) * scale;
			var marker = MapPlotMarkers.Classify(item.Slot, item.Targeted, SelectedRowSlot);
			Color color = marker switch { MapPlotMarker.TableSelection or MapPlotMarker.Both => Color.FromArgb(255, 91, 110), MapPlotMarker.InGameTarget => Color.FromArgb(255, 205, 92), _ => Color.FromArgb(65, 145, 245) };
			float radius = marker == MapPlotMarker.Player ? 4.5f : 7f;
			using var fill = new SolidBrush(color);
			using var ring = new Pen(Color.FromArgb(235, 255, 255, 255), marker == MapPlotMarker.Player ? 1f : 2f);
			g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
			g.DrawEllipse(ring, x - radius - 1, y - radius - 1, (radius + 1) * 2, (radius + 1) * 2);
			if (scale >= 4f || marker != MapPlotMarker.Player)
			{
				using var label = new Font("Segoe UI Semibold", 8f);
				g.DrawString(item.Name, label, Brushes.White, x + 8, y - 8);
			}
		}
		float meX = origin.X + ((float)PlayerX + .5f) * scale;
		float meY = origin.Y + ((float)PlayerY + .5f) * scale;
		using var me = new SolidBrush(Color.FromArgb(34, 211, 174)); using var meRing = new Pen(Color.White, 2f);
		g.FillEllipse(me, meX - 6, meY - 6, 12, 12); g.DrawEllipse(meRing, meX - 7, meY - 7, 14, 14);
	}

	private void DrawHud(Graphics g, int width, int height)
	{
		using var bg = new SolidBrush(Color.FromArgb(225, 14, 19, 26)); using var border = new Pen(Color.FromArgb(55, 255, 255, 255));
		var rect = new RectangleF(14, 14, 300, 58); using var path = Rounded(rect, 10); g.FillPath(bg, path); g.DrawPath(border, path);
		using var title = new Font("Segoe UI Semibold", 10f, FontStyle.Bold); using var small = new Font("Segoe UI", 8.5f);
		g.DrawString($"MAPA {MapNumber?.ToString() ?? "—"}  ·  {width} × {height} tiles", title, Brushes.White, 26, 25);
		g.DrawString($"Você: {PlayerX:0.0}, {PlayerY:0.0}  ·  zoom {_zoom * 100:0}%", small, Brushes.LightGray, 26, 49);
	}

	private void DrawEmpty(Graphics g, string message)
	{
		using var title = new Font("Segoe UI Semibold", 13f, FontStyle.Bold); using var sub = new Font("Segoe UI", 9.5f);
		var box = new Rectangle(30, Height / 2 - 40, Width - 60, 80);
		TextRenderer.DrawText(g, "PREPARANDO MAPA COMPLETO", title, box, Color.White, TextFormatFlags.HorizontalCenter);
		box.Y += 30; TextRenderer.DrawText(g, message, sub, box, Color.FromArgb(145, 158, 177), TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
	}

	private static GraphicsPath Rounded(RectangleF bounds, float radius)
	{
		float d = radius * 2; var path = new GraphicsPath();
		path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90); path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
		path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
	}
}
