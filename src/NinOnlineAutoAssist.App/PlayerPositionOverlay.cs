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
    private readonly ObservedMarkerMotion _motion = new();
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 16 };
    private long? _missingSince;
    private int _missingHoldMs = 350;
    private int _map;
    private string? _localId;
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= WsExTransparent | WsExNoActivate | WsExToolWindow; return cp; }
    }
    public PlayerPositionOverlay()
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual; BackColor = Key; TransparencyKey = Key;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        _animation.Tick += (_, _) =>
        {
            var now = Environment.TickCount64;
            if (_missingSince is { } missing && now - missing >= _missingHoldMs) { ClearDots(); return; }
            if (_motion.IsAnimating(now)) Invalidate();
            else if (_missingSince is null) _animation.Stop();
        };
    }

    public void UpdateDots(Rectangle clientBounds, int map, string localId,
        IReadOnlyList<(NearbyPlayerMarkers.Marker Marker, PointF Point)> dots)
    {
        var contextChanged = _map != map || _localId != localId;
        if (contextChanged)
        { _motion.Reset(); _map = map; _localId = localId; }
        var changed = _missingSince is not null || contextChanged || Bounds != clientBounds || _dots.Count != dots.Count ||
            _dots.Where((old, i) => i < dots.Count &&
                (old.Marker.Slot != dots[i].Marker.Slot || old.Marker.Name != dots[i].Marker.Name ||
                 old.Marker.Selected != dots[i].Marker.Selected ||
                 old.Marker.CurrentlyReported != dots[i].Marker.CurrentlyReported ||
                 old.Marker.MotionAgeMs / 1000 != dots[i].Marker.MotionAgeMs / 1000 ||
                 old.Marker.ObservationAgeMs / 1000 != dots[i].Marker.ObservationAgeMs / 1000 ||
                 Math.Abs(old.Point.X - dots[i].Point.X) > 0.5f ||
                 Math.Abs(old.Point.Y - dots[i].Point.Y) > 0.5f)).Any();
        _missingSince = null;
        if (Bounds != clientBounds) Bounds = clientBounds;
        _dots = dots;
        var now = Environment.TickCount64;
        _motion.KeepOnly(dots.Select(d => d.Marker.Slot).ToHashSet());
        foreach (var (marker, point) in dots)
            _motion.Set(marker.Slot, marker.Name, point.X, point.Y, now, marker.CurrentlyReported);
        if (!Visible) Show();
        if (_motion.IsAnimating(now)) _animation.Start();
        if (changed) Invalidate();
    }

    // A short failed census is not a disappearance or movement observation.
    public void MarkUnavailable(int holdMs = 350)
    {
        if (_dots.Count == 0) return;
        if (_missingSince is null) { _missingSince = Environment.TickCount64; Invalidate(); }
        _missingHoldMs = Math.Clamp(holdMs, 100, 2500);
        _animation.Start();
    }

    // Clear the paint surface without repeatedly destroying/showing the overlay window.
    public void ClearDots()
    {
        _missingSince = null;
        _motion.Reset();
        _animation.Stop();
        if (_dots.Count == 0) return;
        _dots = [];
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Color.Black, 2);
        var now = Environment.TickCount64;
        foreach (var (marker, destination) in _dots)
        {
            var observed = _motion.Position(marker.Slot, now);
            var point = observed is { } p ? new PointF((float)p.X, (float)p.Y) : destination;
            var color = _missingSince is not null || !marker.CurrentlyReported ? Color.Silver : marker.Selected ? Color.Gold : Color.DeepSkyBlue;
            using var brush = new SolidBrush(color);
            e.Graphics.FillEllipse(brush, point.X - 5, point.Y - 5, 10, 10);
            e.Graphics.DrawEllipse(pen, point.X - 6, point.Y - 6, 12, 12);
            var caption = marker.CurrentlyReported
                ? $"{marker.Name} · última pos. (sem mover {marker.MotionAgeMs / 1000}s)"
                : $"{marker.Name} · última pos. há {marker.ObservationAgeMs / 1000.0:0.0}s";
            if (_missingSince is not null) caption += " · leitura em pausa";
            e.Graphics.DrawString(caption, Font, Brushes.Black, point.X + 9, point.Y - 14);
            e.Graphics.DrawString(caption, Font, brush, point.X + 8, point.Y - 15);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
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
