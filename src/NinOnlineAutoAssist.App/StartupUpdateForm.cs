using NinOnlineAutoAssist.Core.Updates;

namespace NinOnlineAutoAssist.App;

internal sealed class StartupUpdateForm : Form
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Label _progress = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Verificando atualizações…" };
    private bool _finished;
    public StartupUpdateResult Result { get; private set; } = new(StartupUpdateOutcome.Canceled, "Abertura cancelada.");
    public StartupUpdateForm(IStartupUpdateBackend backend)
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "Nin Online AutoAssist - Atualização automática";
        ClientSize = new Size(430, 120); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen; MaximizeBox = MinimizeBox = false;
        Controls.Add(_progress);
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
