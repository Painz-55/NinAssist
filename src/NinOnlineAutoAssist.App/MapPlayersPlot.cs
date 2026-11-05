using System.Drawing.Drawing2D;

namespace NinOnlineAutoAssist.App;

/// <summary>Visual-only Cartesian map: origin is local player, +X right, +Y down.</summary>
internal sealed class MapPlayersPlot : Control
{
    public IReadOnlyList<(double X, double Y, bool Selected)> Offsets { get; set; } = [];
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
        var scale = Math.Max(1, Offsets.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y))
            .Select(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y))).DefaultIfEmpty(1).Max());
        var unit = Math.Min((Width - 65) / 2.0, (Height - 35) / 2.0) / scale;
        foreach (var p in Offsets.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
        {
            var x = cx + (float)(p.X * unit); var y = cy + (float)(p.Y * unit);
            g.FillEllipse(p.Selected ? Brushes.Orange : Brushes.DeepSkyBlue, x - 4, y - 4, 8, 8);
        }
        g.FillEllipse(Brushes.White, cx - 3, cy - 3, 6, 6);
    }
}
