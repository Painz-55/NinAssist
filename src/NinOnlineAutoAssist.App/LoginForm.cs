using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Security;

namespace NinOnlineAutoAssist.App;

internal sealed class LoginForm : Form
{
    private readonly FirebaseClient _client;
    private readonly TextBox _email = new() { Width = 350, PlaceholderText = "E-mail cadastrado" };
    private readonly TextBox _password = new() { Width = 350, UseSystemPasswordChar = true, PlaceholderText = "Senha" };
    private readonly Button _login = new() { Text = "ENTRAR", Width = 350, Height = 38 };
    private readonly Label _status = new() { Width = 350, Height = 80 };
    private readonly CancellationTokenSource _stop = new();
    private readonly LoginEmailMemory _emailMemory = new(LoginEmailMemory.DefaultPath);
    private readonly LinkLabel _forgetEmail = new() { Text = "Esquecer e-mail lembrado", AutoSize = true };
    public FirebaseSession? Session { get; private set; }

    public LoginForm(FirebaseClient client, string? updateStatus = null)
    {
        _client = client;
        _status.Text = updateStatus ?? "";
        Text = "Nin Online AutoAssist — Login";
#if LOCAL_ARCHIVE
        Text += " — LOCAL / ARQUIVO";
#endif
        ClientSize = new Size(390, 430); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(16) };
        layout.Controls.Add(new Label { Text = "Acesso concedido pelo administrador", AutoSize = true });
        _email.Text = _emailMemory.Read();
        _forgetEmail.LinkClicked += (_, _) =>
        {
            if (_emailMemory.TryForget()) { _email.Clear(); _status.Text = "E-mail lembrado removido."; _email.Focus(); }
            else _status.Text = "Não foi possível remover o e-mail lembrado neste computador.";
        };
        layout.Controls.Add(_email); layout.Controls.Add(_forgetEmail); layout.Controls.Add(_password); layout.Controls.Add(_login); layout.Controls.Add(_status);
        layout.Controls.Add(new Label { Width = 350, Height = 100,
            Text = "Requer internet. Não há cadastro público no aplicativo.\nContas não isentas registram conta, versão, presença, personagem conectado e trocas no Firebase. Só o último e-mail é lembrado localmente; senha e tokens não são salvos." });
        Controls.Add(layout); AcceptButton = _login;
        _login.Click += async (_, _) => await LoginAsync();
        Shown += (_, _) => { if (_email.Text.Length > 0) _password.Select(); else _email.Select(); };
        FormClosing += (_, _) => { _stop.Cancel(); _password.Clear(); };
    }
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_email.Text) || _password.Text.Length == 0)
        { _status.Text = "Informe e-mail e senha."; return; }
        _login.Enabled = _email.Enabled = _password.Enabled = _forgetEmail.Enabled = false;
        _status.Text = "Validando conta e permissões…";
        try
        {
            var session = await FirebaseSession.LoginAsync(_client, _email.Text.Trim(), _password.Text,
                new SystemMonotonicClock(), Application.ProductVersion, _stop.Token);
            if (IsDisposed || _stop.IsCancellationRequested) { await session.DisposeAsync(); return; }
            _emailMemory.TryRemember(session.Email); // Only after both Auth AND administrator entitlements succeeded.
            Session = session;
            DialogResult = DialogResult.OK; Close();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is AccessDeniedException or FirebaseUnavailableException)
        { if (!IsDisposed) _status.Text = ex.Message; }
        catch { if (!IsDisposed) _status.Text = "Não foi possível entrar. Contate o administrador."; }
        finally
        {
            if (!IsDisposed) { _password.Clear(); _login.Enabled = _email.Enabled = _password.Enabled = _forgetEmail.Enabled = true; }
        }
    }
}
