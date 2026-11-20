using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Security;

namespace NinOnlineAutoAssist.App;

internal sealed class LoginForm : Form
{
    private static readonly Color Bg = Color.FromArgb(13, 15, 19);
    private static readonly Color Panel = Color.FromArgb(22, 26, 33);
    private static readonly Color Field = Color.FromArgb(31, 36, 46);
    private static readonly Color Fg = Color.FromArgb(241, 243, 249);
    private static readonly Color Dim = Color.FromArgb(139, 148, 167);
    private static readonly Color Accent = Color.FromArgb(124, 92, 255);
    private static readonly Color Bad = Color.FromArgb(255, 92, 108);

    private readonly FirebaseClient _client;
    private readonly CancellationTokenSource _stop = new();
    private readonly LoginEmailMemory _emailMemory = new(LoginEmailMemory.DefaultPath);
    private readonly TextBox _email = new() { Dock = DockStyle.Fill, PlaceholderText = "E-mail cadastrado" };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, PlaceholderText = "Senha" };
    private readonly Button _login = new() { Dock = DockStyle.Fill, Text = "ENTRAR", Height = 42 };
    private readonly LinkLabel _forget = new() { Text = "Esquecer e-mail lembrado", AutoSize = true, LinkColor = Accent, ActiveLinkColor = Fg };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };

    public FirebaseSession? Session { get; private set; }

    public LoginForm(FirebaseClient client, string? updateStatus)
    {
        _client = client;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Text = "Nin AutoAssist — Acesso";
        ClientSize = new Size(490, 485);
        MinimumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 10f);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24), BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "NIN AUTOASSIST\nAcesso à central de automação",
            ForeColor = Fg, Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Panel, Padding = new Padding(24, 15, 24, 15) };
        foreach (var height in new[] { 34f, 52f, 52f, 28f, 54f, 46f, 38f }) card.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(new Label { Text = "ENTRE COM SUA CONTA", ForeColor = Dim, Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        StyleField(_email); StyleField(_password);
        card.Controls.Add(_email, 0, 1);
        card.Controls.Add(_password, 0, 2);
        card.Controls.Add(_forget, 0, 3);
        _login.BackColor = Accent; _login.ForeColor = Color.White; _login.FlatStyle = FlatStyle.Flat;
        _login.FlatAppearance.BorderSize = 0; _login.Cursor = Cursors.Hand;
        card.Controls.Add(_login, 0, 4);
        _status.Text = updateStatus ?? "";
        _status.ForeColor = Dim;
        card.Controls.Add(_status, 0, 5);
        card.Controls.Add(new Label { Text = "Conta e recursos exigem autorização do administrador. Contas não isentas registram presença, versão e trocas de personagem no Firebase. Só o e-mail é lembrado; senha e tokens não são salvos.",
            ForeColor = Dim, Dock = DockStyle.Fill }, 0, 7);
        root.Controls.Add(card, 0, 1);
        Controls.Add(root);

        _email.Text = _emailMemory.Read();
        _forget.LinkClicked += (_, _) =>
        {
            if (_emailMemory.TryForget()) { _email.Clear(); ShowStatus("E-mail lembrado removido."); _email.Focus(); }
            else ShowStatus("Não foi possível remover o e-mail lembrado.", true);
        };
        _login.Click += async (_, _) => await LoginAsync();
        AcceptButton = _login;
        Shown += (_, _) => { if (_email.Text.Length > 0) _password.Select(); else _email.Select(); };
        FormClosing += (_, _) => { _stop.Cancel(); _password.Clear(); };
    }

    private static void StyleField(TextBox field)
    {
        field.BackColor = Field; field.ForeColor = Fg; field.BorderStyle = BorderStyle.FixedSingle;
        field.Margin = new Padding(0, 5, 0, 8); field.Font = new Font("Segoe UI", 11f);
    }

    private void ShowStatus(string text, bool error = false)
    { _status.Text = text; _status.ForeColor = error ? Bad : Dim; }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_email.Text) || _password.Text.Length == 0)
        { ShowStatus("Informe e-mail e senha.", true); return; }
        _login.Enabled = _email.Enabled = _password.Enabled = _forget.Enabled = false;
        ShowStatus("Validando conta e permissões…");
        try
        {
            var session = await FirebaseSession.LoginAsync(_client, _email.Text.Trim(), _password.Text,
                new SystemMonotonicClock(), Application.ProductVersion, _stop.Token);
            if (IsDisposed || _stop.IsCancellationRequested) { await session.DisposeAsync(); return; }
            _emailMemory.TryRemember(session.Email);
            Session = session;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is AccessDeniedException or FirebaseUnavailableException)
        { if (!IsDisposed) ShowStatus(ex.Message, true); }
        catch { if (!IsDisposed) ShowStatus("Não foi possível entrar. Contate o administrador.", true); }
        finally
        {
            if (!IsDisposed) { _password.Clear(); _login.Enabled = _email.Enabled = _password.Enabled = _forget.Enabled = true; }
        }
    }
}
