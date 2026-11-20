using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Updates;

namespace NinOnlineAutoAssist.App;

internal sealed class StartupUpdateForm : Form
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Label _progress = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Verificando atualizações…",
        ForeColor = Color.FromArgb(241, 243, 249), Font = new Font("Segoe UI", 10f) };
    private bool _finished;
    public StartupUpdateResult Result { get; private set; } = new(StartupUpdateOutcome.Canceled, "Abertura cancelada.");
    public StartupUpdateForm(IStartupUpdateBackend backend)
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "Nin AutoAssist — Atualização";
        ClientSize = new Size(490, 160); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen; MaximizeBox = MinimizeBox = false;
        BackColor = Color.FromArgb(13, 15, 19);
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(24), BackColor = BackColor };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label { Text = "NIN AUTOASSIST  /  GITHUB RELEASES", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(124, 92, 255), Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold) }, 0, 0);
        card.Controls.Add(_progress, 0, 1);
        Controls.Add(card);
        FormClosing += (_, _) => { if (!_finished) _stop.Cancel(); };
        Shown += async (_, _) =>
        {
            var result = await StartupUpdateRunner.RunAsync(backend, ShowProgress, _stop.Token);
            if (IsDisposed || Disposing) return;
            Result = result; _finished = true; DialogResult = DialogResult.OK; Close();
        };
    }
    private void ShowProgress(string text)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => ShowProgress(text)); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
        }
        else _progress.Text = text;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { _stop.Cancel(); _stop.Dispose(); }
        base.Dispose(disposing);
    }
}
