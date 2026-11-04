namespace NinOnlineAutoAssist.Core.Updates;

public interface IStartupUpdateBackend
{
    bool IsInstalled { get; }
    Task<bool> CheckAsync(CancellationToken cancellation);
    Task DownloadAsync(Action<int> progress, CancellationToken cancellation);
    void ApplyAndRestart();
}

public enum StartupUpdateOutcome { Continue, RestartRequested, Canceled, Blocked }
public sealed record StartupUpdateResult(StartupUpdateOutcome Outcome, string Message);

/// <summary>Always automatic; run ONLY at startup, before authentication/connection or held input exists.</summary>
public static class StartupUpdateRunner
{
    public static async Task<StartupUpdateResult> RunAsync(IStartupUpdateBackend backend, Action<string> progress, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!backend.IsInstalled) return new(StartupUpdateOutcome.Blocked, "Instalação obrigatória. Instale pelo Setup oficial e abra pelo atalho.");
            // Even a cached/pending package must pass a fresh online check. Never use an offline bypass.
            progress("Verificando atualizações obrigatórias…");
            var available = await backend.CheckAsync(cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (!available)
                return new(StartupUpdateOutcome.Continue, "Verificação concluída: versão atual. Login liberado.");
            progress("Baixando atualização obrigatória…");
            await backend.DownloadAsync(p => progress($"Baixando atualização: {Math.Clamp(p, 0, 100)}%"), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            progress("Aplicando atualização e reiniciando…");
            backend.ApplyAndRestart();
            return new(StartupUpdateOutcome.RestartRequested, "Atualização em aplicação.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { return new(StartupUpdateOutcome.Canceled, "Abertura cancelada."); }
        catch
        {
            if (cancellation.IsCancellationRequested) return new(StartupUpdateOutcome.Canceled, "Abertura cancelada.");
            // Do not expose HTTP URLs, response bodies or accidental credentials. Never apply after a failed download.
            return new(StartupUpdateOutcome.Blocked, "Não foi possível verificar ou concluir a atualização obrigatória. O programa não será aberto. Confira a internet e tente novamente pelo atalho.");
        }
    }
}
