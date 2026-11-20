using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NinOnlineAutoAssist.App;

internal sealed class ModuleCardButton : Button
{
	private bool _hovered;

	public ModuleCardButton()
	{
		DoubleBuffered = true;
		FlatStyle = FlatStyle.Flat;
		FlatAppearance.BorderSize = 0;
		Cursor = Cursors.Hand;
		TabStop = false;
		UseVisualStyleBackColor = false;
	}

	protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
	protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		bool active = Tag is bool value && value;
		Color surface = !Enabled ? Color.FromArgb(20, 23, 29) : active ? Color.FromArgb(24, 49, 45) : (_hovered ? Color.FromArgb(37, 43, 54) : Color.FromArgb(27, 32, 41));
		Color accent = !Enabled ? Color.FromArgb(75, 82, 96) : active ? Color.FromArgb(34, 211, 166) : Color.FromArgb(124, 92, 255);
		using var path = Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 10);
		using var fill = new SolidBrush(surface);
		using var border = new Pen(active ? Color.FromArgb(88, accent) : Color.FromArgb(47, 56, 70), 1f);
		g.FillPath(fill, path);
		g.DrawPath(border, path);
		using var accentBrush = new SolidBrush(accent);
		g.FillRectangle(accentBrush, 1, 12, 3, Math.Max(12, Height - 24));
		g.FillEllipse(accentBrush, 14, 16, 8, 8);

		string[] lines = Text.Split('\n', 2);
		string title = lines.Length > 0 ? lines[0] : Text;
		string status = lines.Length > 1 ? lines[1] : (active ? "ATIVO" : "INATIVO");
		using var titleFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
		using var statusFont = new Font("Segoe UI", 8f);
		Color titleColor = Enabled ? Color.FromArgb(239, 242, 248) : Color.FromArgb(104, 111, 124);
		Color statusColor = Enabled ? (active ? accent : Color.FromArgb(139, 148, 167)) : Color.FromArgb(82, 88, 99);
		TextRenderer.DrawText(g, title, titleFont, new Rectangle(28, 10, Width - 36, 36), titleColor,
			TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
		TextRenderer.DrawText(g, status, statusFont, new Rectangle(14, Height - 28, Width - 24, 20), statusColor,
			TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
	}

	private static GraphicsPath Rounded(RectangleF bounds, float radius)
	{
		float d = radius * 2;
		var path = new GraphicsPath();
		path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
		path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
		path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
		path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
		path.CloseFigure();
		return path;
	}
}
