using System.Text.Json;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Core.Updates;
using Velopack;
using System.Reflection;

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
#if LOCAL_ARCHIVE
            // Separate maintainer archive executable, never enabled by a config file or command-line flag.
            // Authentication and individual Firebase authorization below remain mandatory.
            string updateStatus = "BUILD LOCAL / ARQUIVO — sem updater. Login Firebase obrigatório.";
#else
            string updateStatus;
            try
            {
                // Feed is embedded at build time, not controlled by an editable JSON or command-line switch.
                var feed = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(a => a.Key == "UpdateRepositoryUrl").Value;
                var repository = new AutomaticUpdateConfig { RepositoryUrl = feed ?? "" }.ValidatedRepositoryUrl()
                    ?? throw new InvalidOperationException("Missing update feed");
                using var updater = new StartupUpdateForm(new VelopackUpdateBackend(repository));
                updater.ShowDialog();
                if (updater.Result.Outcome == StartupUpdateOutcome.Blocked)
                    MessageBox.Show(updater.Result.Message, "Atualização obrigatória", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (updater.Result.Outcome != StartupUpdateOutcome.Continue) return;
                updateStatus = updater.Result.Message;
            }
            catch
            {
                MessageBox.Show("A atualização obrigatória não pôde ser iniciada. Reinstale pelo Setup oficial. O programa não será aberto.",
                    "Atualização obrigatória", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
#endif
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
