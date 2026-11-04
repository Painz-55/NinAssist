using NinOnlineAutoAssist.App;
using Velopack;
using Velopack.Logging;
using Velopack.NuGet;
using Velopack.Sources;

namespace NinOnlineAutoAssist.Tests;

public class RequiredReleaseSourceTests
{
    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.1.1")]
    public async Task CurrentOrNewerPublishedFullReleasePasses(string remote)
    {
        var source = Create(Asset(remote));
        var feed = await source.GetReleaseFeed(null!, "NinOnlineAutoAssist", "win", null, null);
        Assert.Single(feed.Assets);
    }
    [Fact]
    public async Task EmptyFeedCannotAuthorizeLogin() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create().GetReleaseFeed(null!, "NinOnlineAutoAssist", "win", null, null));

    [Theory]
    [InlineData("1.0.0", "NinOnlineAutoAssist", VelopackAssetType.Full)]
    [InlineData("1.1.0", "OtherApp", VelopackAssetType.Full)]
    [InlineData("1.1.0", "NinOnlineAutoAssist", VelopackAssetType.Delta)]
    [InlineData("1.1.0", "NinOnlineAutoAssist", VelopackAssetType.Installer)]
    public async Task OldWrongOrIncompleteFeedCannotAuthorizeLogin(string version, string id, VelopackAssetType type)
    {
        var asset = Asset(version); asset.PackageId = id; asset.Type = type;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(asset).GetReleaseFeed(null!, "NinOnlineAutoAssist", "win", null, null));
    }
    [Fact]
    public async Task UntrustedCachedVersionDoesNotReplaceInstalledVersion()
    {
        // A pending package is not the installed version. The live feed must be compared with the installed assembly.
        var feed = await Create(Asset("1.1.0")).GetReleaseFeed(null!, "NinOnlineAutoAssist", "win", null, Asset("9.0.0"));
        Assert.Single(feed.Assets);
    }
    private static VelopackUpdateBackend.RequiredReleaseSource Create(params VelopackAsset[] assets) =>
        new(new FakeSource(assets), () => SemanticVersion.Parse("1.1.0"));
    private static VelopackAsset Asset(string version) => new()
    {
        PackageId = "NinOnlineAutoAssist", Version = SemanticVersion.Parse(version), Type = VelopackAssetType.Full
    };
    private sealed class FakeSource(VelopackAsset[] assets) : IUpdateSource
    {
        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId, VelopackAsset? latestLocalRelease) =>
            Task.FromResult(new VelopackAssetFeed { Assets = assets });
        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken) =>
            throw new InvalidOperationException("No downloads in feed tests");
    }
}
