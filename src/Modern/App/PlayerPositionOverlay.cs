using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

internal sealed class PlayerPositionOverlay : Form
{
	private struct NativePoint
	{
		public int X;

		public int Y;
	}

	private struct NativeRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private const int WsExTransparent = 32;

	private const int WsExNoActivate = 134217728;

	private const int WsExToolWindow = 128;

	private static readonly Color Key = Color.FromArgb(255, 0, 255);

	private IReadOnlyList<(NearbyPlayerMarkers.Marker Marker, PointF Point)> _dots = Array.Empty<(NearbyPlayerMarkers.Marker, PointF)>();

	private readonly ObservedMarkerMotion _motion = new ObservedMarkerMotion();

	private readonly Timer _animation = new Timer
	{
		Interval = 16
	};

	private long? _missingSince;

	private int _missingHoldMs = 350;

	private int _map;

	private string? _localId;

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ExStyle |= 134217888;
			return createParams;
		}
	}

	public PlayerPositionOverlay()
	{
		base.FormBorderStyle = FormBorderStyle.None;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = FormStartPosition.Manual;
		BackColor = Key;
		base.TransparencyKey = Key;
		DoubleBuffered = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		_animation.Tick += delegate
		{
			long tickCount = Environment.TickCount64;
			long? missingSince = _missingSince;
			if (missingSince.HasValue)
			{
				long valueOrDefault = missingSince.GetValueOrDefault();
				if (tickCount - valueOrDefault >= _missingHoldMs)
				{
					ClearDots();
					return;
				}
			}
			if (_motion.IsAnimating(tickCount))
			{
				Invalidate();
			}
			else
			{
				missingSince = _missingSince;
				if (!missingSince.HasValue)
				{
					_animation.Stop();
				}
			}
		};
	}

	public void UpdateDots(Rectangle clientBounds, int map, string localId, IReadOnlyList<(NearbyPlayerMarkers.Marker Marker, PointF Point)> dots)
	{
		bool flag = _map != map || _localId != localId;
		if (flag)
		{
			_motion.Reset();
			_map = map;
			_localId = localId;
		}
		long? missingSince = _missingSince;
		bool flag2 = missingSince.HasValue || flag || base.Bounds != clientBounds || _dots.Count != dots.Count || _dots.Where<(NearbyPlayerMarkers.Marker, PointF)>(((NearbyPlayerMarkers.Marker Marker, PointF Point) old, int i) => i < dots.Count && (old.Marker.Slot != dots[i].Marker.Slot || old.Marker.Name != dots[i].Marker.Name || old.Marker.Selected != dots[i].Marker.Selected || old.Marker.CurrentlyReported != dots[i].Marker.CurrentlyReported || old.Marker.MotionAgeMs / 1000 != dots[i].Marker.MotionAgeMs / 1000 || old.Marker.ObservationAgeMs / 1000 != dots[i].Marker.ObservationAgeMs / 1000 || Math.Abs(old.Point.X - dots[i].Point.X) > 0.5f || Math.Abs(old.Point.Y - dots[i].Point.Y) > 0.5f)).Any();
		_missingSince = null;
		if (base.Bounds != clientBounds)
		{
			base.Bounds = clientBounds;
		}
		_dots = dots;
		long tickCount = Environment.TickCount64;
		_motion.KeepOnly(dots.Select<(NearbyPlayerMarkers.Marker, PointF), int>(((NearbyPlayerMarkers.Marker Marker, PointF Point) d) => d.Marker.Slot).ToHashSet());
		foreach (var (marker, pointF) in dots)
		{
			_motion.Set(marker.Slot, marker.Name, pointF.X, pointF.Y, tickCount, marker.CurrentlyReported);
		}
		if (!base.Visible)
		{
			Show();
		}
		if (_motion.IsAnimating(tickCount))
		{
			_animation.Start();
		}
		if (flag2)
		{
			Invalidate();
		}
	}

	public void MarkUnavailable(int holdMs = 350)
	{
		if (_dots.Count != 0)
		{
			long? missingSince = _missingSince;
			if (!missingSince.HasValue)
			{
				_missingSince = Environment.TickCount64;
				Invalidate();
			}
			_missingHoldMs = Math.Clamp(holdMs, 100, 2500);
			_animation.Start();
		}
	}

	public void ClearDots()
	{
		_missingSince = null;
		_motion.Reset();
		_animation.Stop();
		if (_dots.Count != 0)
		{
			_dots = Array.Empty<(NearbyPlayerMarkers.Marker, PointF)>();
			Invalidate();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		base.OnPaint(e);
		e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using Pen pen = new Pen(Color.Black, 2f);
		long tickCount = Environment.TickCount64;
		foreach (var dot in _dots)
		{
			NearbyPlayerMarkers.Marker item = dot.Marker;
			PointF item2 = dot.Point;
			(double, double)? tuple = _motion.Position(item.Slot, tickCount);
			PointF pointF;
			if (tuple.HasValue)
			{
				(double, double) valueOrDefault = tuple.GetValueOrDefault();
				pointF = new PointF((float)valueOrDefault.Item1, (float)valueOrDefault.Item2);
			}
			else
			{
				pointF = item2;
			}
			PointF pointF2 = pointF;
			long? missingSince = _missingSince;
			using SolidBrush brush = new SolidBrush((missingSince.HasValue || !item.CurrentlyReported) ? Color.Silver : (item.Selected ? Color.Gold : Color.DeepSkyBlue));
			e.Graphics.FillEllipse(brush, pointF2.X - 5f, pointF2.Y - 5f, 10f, 10f);
			e.Graphics.DrawEllipse(pen, pointF2.X - 6f, pointF2.Y - 6f, 12f, 12f);
			string text = (item.CurrentlyReported ? $"{item.Name} · última pos. (sem mover {item.MotionAgeMs / 1000}s)" : $"{item.Name} · última pos. há {(double)item.ObservationAgeMs / 1000.0:0.0}s");
			missingSince = _missingSince;
			if (missingSince.HasValue)
			{
				text += " · leitura em pausa";
			}
			e.Graphics.DrawString(text, Font, Brushes.Black, pointF2.X + 9f, pointF2.Y - 14f);
			e.Graphics.DrawString(text, Font, brush, pointF2.X + 8f, pointF2.Y - 15f);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			_animation.Dispose();
		}
		base.Dispose(disposing);
	}

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(nint window, out NativeRect rect);

	[DllImport("user32.dll")]
	private static extern bool ClientToScreen(nint window, ref NativePoint point);

	public static Rectangle? GameClientBounds(int pid)
	{
		try
		{
			using Process process = Process.GetProcessById(pid);
			nint mainWindowHandle = process.MainWindowHandle;
			if (mainWindowHandle == IntPtr.Zero || !GetClientRect(mainWindowHandle, out var rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
			{
				return null;
			}
			NativePoint point = default(NativePoint);
			if (!ClientToScreen(mainWindowHandle, ref point))
			{
				return null;
			}
			return new Rectangle(point.X, point.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
		}
		catch
		{
			return null;
		}
	}
}
