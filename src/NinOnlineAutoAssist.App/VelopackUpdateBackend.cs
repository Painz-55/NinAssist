using NinOnlineAutoAssist.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace NinOnlineAutoAssist.App;

internal sealed class VelopackUpdateBackend : IStartupUpdateBackend
{
    private readonly UpdateManager _manager;
    private UpdateInfo? _offer;
    public VelopackUpdateBackend(string validatedRepositoryUrl) => _manager = new UpdateManager(
        new GithubSource(validatedRepositoryUrl, accessToken: null, prerelease: false),
        new UpdateOptions { AllowVersionDowngrade = false });
    public bool IsInstalled => _manager.IsInstalled;
    public bool HasPendingUpdate => _manager.UpdatePendingRestart is { } pending && pending.Version > _manager.CurrentVersion;
    public async Task<bool> CheckAsync(CancellationToken cancellation)
    {
        _offer = await _manager.CheckForUpdatesAsync().WaitAsync(cancellation).ConfigureAwait(false);
        return _offer is not null && _offer.TargetFullRelease.Version > _manager.CurrentVersion;
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken cancellation) =>
        _manager.DownloadUpdatesAsync(_offer ?? throw new InvalidOperationException("No update offer"), progress, cancellation);
    public void ApplyAndRestart()
    {
        var asset = _offer?.TargetFullRelease ?? _manager.UpdatePendingRestart;
        if (!IsInstalled || asset is null || asset.Version <= _manager.CurrentVersion) throw new InvalidOperationException("No newer update");
        _manager.ApplyUpdatesAndRestart(asset);
    }
}
