using System.Text.Json;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Core.Updates;
using Velopack;

namespace NinOnlineAutoAssist.App;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // First, exactly once. Disable implicit apply so settings are migrated BEFORE replacing current/.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(false, "Local\\NinOnlineAutoAssist.App");
        bool owned;
        try { owned = instance.WaitOne(0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned)
        {
            MessageBox.Show("O assistente já está aberto. Feche a outra instância antes de iniciar ou atualizar.", "Nin Online AutoAssist");
            return;
        }
        try { RunApplication(); }
        finally { instance.ReleaseMutex(); }
    }

    private static void RunApplication()
    {
        try
        {
            AppPaths.Settings.EnsureConfig();
            var updateStatus = "Atualizador aguardando configuração do repositório.";
            try
            {
                var updatePath = Path.Combine(AppContext.BaseDirectory, "updates.config.json");
                var updates = File.Exists(updatePath) ? JsonSerializer.Deserialize<AutomaticUpdateConfig>(File.ReadAllText(updatePath)) : null;
                if (updates?.ValidatedRepositoryUrl() is { } repository)
                {
                    using var updater = new StartupUpdateForm(new VelopackUpdateBackend(repository));
                    updater.ShowDialog();
                    if (updater.Result.Outcome != StartupUpdateOutcome.Continue) return;
                    updateStatus = updater.Result.Message;
                }
            }
            catch { updateStatus = "Atualizador indisponível/não configurado; login Firebase continua obrigatório."; }
            var config = JsonSerializer.Deserialize<FirebaseConfig>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "firebase.config.json")))
                ?? throw new AccessDeniedException("Configuração Firebase ausente.");
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
            var client = new FirebaseClient(http, config);
            while (true)
            {
                using var login = new LoginForm(client, updateStatus);
                if (login.ShowDialog() != DialogResult.OK || login.Session is not { } session) break;
                try
                {
                    using var main = new Form1(session);
                    Application.Run(main);
                    if (!main.ReturnToLogin) break;
                }
                finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
        }
        catch
        {
            MessageBox.Show("Não foi possível iniciar com autenticação. Confira firebase.config.json e contate o administrador.",
                "Nin Online AutoAssist", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }    
}
