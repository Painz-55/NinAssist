using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Core.Updates;
using Velopack;

namespace NinOnlineAutoAssist.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Velopack startup hooks must run before creating any window. Settings migrate before applying updates.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(false, "Local\\NinOnlineAutoAssist.App");
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            MessageBox.Show("O assistente já está aberto. Feche a outra instância antes de atualizar.", "Nin AutoAssist");
            return;
        }
        try { RunApplication(); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void RunApplication()
    {
        try
        {
            AppPaths.Settings.EnsureConfig();
            var repository = new AutomaticUpdateConfig
            {
                RepositoryUrl = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(a => a.Key == "UpdateRepositoryUrl").Value ?? ""
            }.ValidatedRepositoryUrl() ?? throw new InvalidOperationException("Feed de atualizações inválido.");
            using var updater = new StartupUpdateForm(new VelopackUpdateBackend(repository));
            updater.ShowDialog();
            if (updater.Result.Outcome == StartupUpdateOutcome.Blocked)
                MessageBox.Show(updater.Result.Message, "Atualização obrigatória", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (updater.Result.Outcome != StartupUpdateOutcome.Continue) return;

            var path = Path.Combine(AppContext.BaseDirectory, "firebase.config.json");
            var config = JsonSerializer.Deserialize<FirebaseConfig>(File.ReadAllText(path))
                ?? throw new AccessDeniedException("Configuração Firebase ausente.");
            using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = TimeSpan.FromSeconds(12) };
            var client = new FirebaseClient(http, config);
            while (true)
            {
                using var login = new LoginForm(client, updater.Result.Message);
                if (login.ShowDialog() != DialogResult.OK || login.Session is not { } session) return;
                try
                {
                    using var main = new Form1(session);
                    Application.Run(main);
                    if (!main.ReturnToLogin) return;
                }
                finally { session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            }
        }
        catch
        {
            MessageBox.Show("Não foi possível iniciar com autenticação e atualização verificadas. Confira a instalação e tente novamente pelo atalho.",
                "Nin AutoAssist", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
