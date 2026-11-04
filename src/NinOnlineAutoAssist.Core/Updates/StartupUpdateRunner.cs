namespace NinOnlineAutoAssist.Core.Updates;

public interface IStartupUpdateBackend
{
    bool IsInstalled { get; }
    bool HasPendingUpdate { get; }
    Task<bool> CheckAsync(CancellationToken cancellation);
    Task DownloadAsync(Action<int> progress, CancellationToken cancellation);
    void ApplyAndRestart();
}

public enum StartupUpdateOutcome { Continue, RestartRequested, Canceled }
public sealed record StartupUpdateResult(StartupUpdateOutcome Outcome, string Message);

/// <summary>Always automatic; run ONLY at startup, before authentication/connection or held input exists.</summary>
public static class StartupUpdateRunner
{
    public static async Task<StartupUpdateResult> RunAsync(IStartupUpdateBackend backend, Action<string> progress, CancellationToken cancellation)
    {
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!backend.IsInstalled) return new(StartupUpdateOutcome.Continue, "Use o instalador para receber atualizações automáticas.");
            if (!backend.HasPendingUpdate)
            {
                progress("Verificando atualizações…");
                var available = await backend.CheckAsync(cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (!available)
                    return new(StartupUpdateOutcome.Continue, "Atualização automática: versão atual.");
                progress("Baixando atualização…");
                await backend.DownloadAsync(p => progress($"Baixando atualização: {Math.Clamp(p, 0, 100)}%"), cancellation).ConfigureAwait(false);
            }
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
            return new(StartupUpdateOutcome.Continue, "Falha na atualização automática; mantendo a versão atual. Login Firebase continua obrigatório.");
        }
    }
}
