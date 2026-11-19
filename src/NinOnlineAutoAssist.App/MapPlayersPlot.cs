using System.Drawing.Drawing2D;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

/// <summary>Visual-only Cartesian map: origin is local player, +X right, +Y down.</summary>
internal sealed class MapPlayersPlot : Control
{
    public IReadOnlyList<(int Slot, double X, double Y, bool Targeted)> Offsets { get; set; } = [];
    public int? SelectedRowSlot { get; set; }
    public bool SelectedHighlightOn { get; set; }
    public bool HasSelectedRow => SelectedRowSlot is { } slot && Offsets.Any(p => p.Slot == slot);
    private double _displayRadius = 10;
    public void ResetScale() { _displayRadius = 10; Offsets = []; Invalidate(); }
    public MapPlayersPlot() { DoubleBuffered = true; Dock = DockStyle.Fill; }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(13, 15, 19));
        var cx = Width / 2f; var cy = Height / 2f;
        using var axis = new Pen(Color.DimGray);
        g.DrawLine(axis, 0, cy, Width, cy); g.DrawLine(axis, cx, 0, cx, Height);
        g.DrawString("−X", Font, Brushes.LightGray, 2, cy + 3);
        g.DrawString("+X", Font, Brushes.LightGray, Width - 30, cy + 3);
        g.DrawString("−Y", Font, Brushes.LightGray, cx + 3, 1);
        g.DrawString("+Y", Font, Brushes.LightGray, cx + 3, Height - 18);
        // A new nearby player must not re-zoom the whole plot on every census.
        var needed = Offsets.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y))
            .Select(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y))).DefaultIfEmpty(1).Max();
        if (needed > _displayRadius) _displayRadius = Math.Pow(2, Math.Ceiling(Math.Log2(needed)));
        var unit = Math.Min((Width - 65) / 2.0, (Height - 35) / 2.0) / _displayRadius;
        foreach (var p in Offsets.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
        {
            var x = cx + (float)(p.X * unit); var y = cy + (float)(p.Y * unit);
            var marker = MapPlotMarkers.Classify(p.Slot, p.Targeted, SelectedRowSlot);
            if (marker is MapPlotMarker.TableSelection or MapPlotMarker.Both)
            {
                g.FillEllipse(Brushes.DarkRed, x - 6, y - 6, 12, 12);
                using var outline = new Pen(Color.Red, 2);
                g.DrawEllipse(outline, x - 8, y - 8, 16, 16);
                if (marker == MapPlotMarker.Both) g.FillEllipse(Brushes.Gold, x - 2, y - 2, 4, 4);
            }
            else if (marker == MapPlotMarker.InGameTarget) g.FillEllipse(Brushes.Gold, x - 5, y - 5, 10, 10);
            else g.FillEllipse(Brushes.DeepSkyBlue, x - 4, y - 4, 8, 8);
        }
        g.FillEllipse(Brushes.White, cx - 3, cy - 3, 6, 6);
    }
}
