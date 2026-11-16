using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

/// <summary>Click-through visual-only window. Coordinates come from the read-only map projection.</summary>
internal sealed class PlayerPositionOverlay : Form
{
    private const int WsExTransparent = 0x20, WsExNoActivate = 0x08000000, WsExToolWindow = 0x80;
    private static readonly Color Key = Color.FromArgb(255, 0, 255);
    private IReadOnlyList<(NearbyPlayerMarkers.Marker Marker, PointF Point)> _dots = [];
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= WsExTransparent | WsExNoActivate | WsExToolWindow; return cp; }
    }
    public PlayerPositionOverlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; BackColor = Key; TransparencyKey = Key;
    }

    public void UpdateDots(Rectangle clientBounds, IReadOnlyList<(NearbyPlayerMarkers.Marker Marker, PointF Point)> dots)
    {
        Bounds = clientBounds;
        _dots = dots;
        if (dots.Count == 0) { Hide(); return; }
        if (!Visible) Show();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.Black, 2);
        foreach (var (marker, point) in _dots)
        {
            var color = !marker.CurrentlyReported ? Color.Silver : marker.Selected ? Color.Gold : Color.DeepSkyBlue;
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, point.X - 5, point.Y - 5, 10, 10);
            e.Graphics.DrawEllipse(pen, point.X - 6, point.Y - 6, 12, 12);
            var caption = marker.CurrentlyReported
                ? $"{marker.Name} · última pos. (sem mover {marker.MotionAgeMs / 1000}s)"
                : $"{marker.Name} · última pos. há {marker.ObservationAgeMs / 1000.0:0.0}s";
            e.Graphics.DrawString(caption, Font, Brushes.Black, point.X + 9, point.Y - 14);
            e.Graphics.DrawString(caption, Font, brush, point.X + 8, point.Y - 15);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    public static Rectangle? GameClientBounds(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            var window = process.MainWindowHandle;
            if (window == IntPtr.Zero || !GetClientRect(window, out var rect) ||
                rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;
            var point = new NativePoint();
            if (!ClientToScreen(window, ref point)) return null;
            return new Rectangle(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        catch { return null; }
    }
}
