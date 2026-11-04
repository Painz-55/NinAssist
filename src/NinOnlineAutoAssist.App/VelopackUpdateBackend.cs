using NinOnlineAutoAssist.Core.Updates;
using Velopack;
using Velopack.Sources;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.NuGet;

namespace NinOnlineAutoAssist.App;

internal sealed class VelopackUpdateBackend : IStartupUpdateBackend
{
    private readonly VerifiedUpdateManager _manager;
    private UpdateInfo? _offer;
    public VelopackUpdateBackend(string validatedRepositoryUrl) => _manager = new VerifiedUpdateManager(
        new RequiredReleaseSource(new GithubSource(validatedRepositoryUrl, accessToken: null, prerelease: false),
            () => VelopackLocator.Current.CurrentlyInstalledVersion));
    public bool IsInstalled => _manager.IsInstalled && !VelopackLocator.Current.IsPortable;
    public async Task<bool> CheckAsync(CancellationToken cancellation)
    {
        _offer = await _manager.CheckForUpdatesAsync().WaitAsync(cancellation).ConfigureAwait(false);
        return _offer is not null && _offer.TargetFullRelease.Version > _manager.CurrentVersion;
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken cancellation) =>
        _manager.DownloadUpdatesAsync(_offer ?? throw new InvalidOperationException("No update offer"), progress, cancellation);
    public void ApplyAndRestart()
    {
        var asset = _offer?.TargetFullRelease;
        if (!IsInstalled || asset is null || asset.Version <= _manager.CurrentVersion) throw new InvalidOperationException("No newer update");
        _manager.ApplyUpdatesAndRestart(asset);
    }

    // An empty/deleted/incompatible feed is NOT proof that this installation is current.
    private sealed class VerifiedUpdateManager(IUpdateSource source) : UpdateManager(source, new UpdateOptions { AllowVersionDowngrade = false })
    {
        public override async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
        {
            await base.DownloadUpdatesAsync(updates, progress, cancelToken).ConfigureAwait(false);
            cancelToken.ThrowIfCancellationRequested();
            // The SDK skips validation when a completed package already exists. Verify cached packages too.
            await VerifyPackageChecksumAsync(updates.TargetFullRelease).ConfigureAwait(false);
            cancelToken.ThrowIfCancellationRequested();
        }
    }

    internal sealed class RequiredReleaseSource(IUpdateSource source, Func<SemanticVersion?> currentVersion) : IUpdateSource
    {
        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease)
        {
            var feed = await source.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease).ConfigureAwait(false);
            var installedVersion = currentVersion();
            if (installedVersion is null || feed.Assets.Any(a => a.PackageId != appId) ||
                !feed.Assets.Any(a => a.Type == VelopackAssetType.Full && a.Version >= installedVersion))
                throw new InvalidOperationException("No valid published release for this installation");
            return feed;
        }
        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken) =>
            source.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
