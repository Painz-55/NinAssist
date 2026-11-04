using NinOnlineAutoAssist.Core.Updates;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Tests;

public class AutomaticUpdateTests
{
    [Theory]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("https://github.com/example/releases/", "https://github.com/example/releases")]
    [InlineData("https://github.com/example/releases.git", "https://github.com/example/releases")]
    public void ValidatesFeed(string input, string? expected) =>
        Assert.Equal(expected, new AutomaticUpdateConfig { RepositoryUrl = input }.ValidatedRepositoryUrl());

    [Theory]
    [InlineData("http://github.com/example/releases")]
    [InlineData("https://token@github.com/example/releases")]
    [InlineData("https://github.com/example/releases?token=secret")]
    [InlineData("https://github.com/example/releases#secret")]
    [InlineData("https://github.com.evil.test/example/releases")]
    [InlineData("https://github.com:444/example/releases")]
    [InlineData("https://github.com/example/releases/latest")]
    [InlineData("https://github.com/example/.git")]
    public void RejectsUnsafeFeed(string input) => Assert.Throws<ArgumentException>(() =>
        new AutomaticUpdateConfig { RepositoryUrl = input }.ValidatedRepositoryUrl());

    [Fact]
    public async Task AutomaticallyChecksDownloadsThenApplies()
    {
        var backend = new FakeBackend();
        var result = await StartupUpdateRunner.RunAsync(backend, _ => { }, default);
        Assert.Equal(StartupUpdateOutcome.RestartRequested, result.Outcome);
        Assert.Equal(new[] { "check", "download", "apply" }, backend.Calls);
    }
    [Fact]
    public async Task EveryStartupChecksAgainBeforeAllowingLogin()
    {
        var backend = new FakeBackend { Available = false };
        for (var i = 0; i < 2; i++)
            Assert.Equal(StartupUpdateOutcome.Continue, (await StartupUpdateRunner.RunAsync(backend, _ => { }, default)).Outcome);
        Assert.Equal(new[] { "check", "check" }, backend.Calls);
    }
    [Theory]
    [InlineData("check")]
    [InlineData("download")]
    [InlineData("apply")]
    public async Task AnyFailureBlocksLoginAndDoesNotExposeException(string failure)
    {
        var backend = new FakeBackend { Failure = failure };
        var result = await StartupUpdateRunner.RunAsync(backend, _ => { }, default);
        Assert.Equal(StartupUpdateOutcome.Blocked, result.Outcome);
        Assert.DoesNotContain("secret", result.Message);
        if (failure != "apply") Assert.DoesNotContain("apply", backend.Calls);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task NoInstallationOrNoOfferNeverApplies(bool installed, bool available)
    {
        var backend = new FakeBackend { IsInstalled = installed, Available = available };
        Assert.Equal(installed ? StartupUpdateOutcome.Continue : StartupUpdateOutcome.Blocked,
            (await StartupUpdateRunner.RunAsync(backend, _ => { }, default)).Outcome);
        Assert.Equal(installed ? new[] { "check" } : Array.Empty<string>(), backend.Calls);
        Assert.DoesNotContain("download", backend.Calls);
        Assert.DoesNotContain("apply", backend.Calls);
    }
    [Theory]
    [InlineData("before")]
    [InlineData("check")]
    [InlineData("download")]
    public async Task CancellationNeverApplies(string stage)
    {
        using var cts = new CancellationTokenSource();
        var backend = new FakeBackend { AfterCall = call => { if (call == stage) cts.Cancel(); } };
        if (stage == "before") cts.Cancel();
        var result = await StartupUpdateRunner.RunAsync(backend, _ => { }, cts.Token);
        Assert.Equal(StartupUpdateOutcome.Canceled, result.Outcome);
        Assert.DoesNotContain("apply", backend.Calls);
    }
    [Fact]
    public async Task EarlierSuccessfulCheckDoesNotAuthorizeAnotherStartupWhenOffline()
    {
        var backend = new FakeBackend { Available = false };
        Assert.Equal(StartupUpdateOutcome.Continue, (await StartupUpdateRunner.RunAsync(backend, _ => { }, default)).Outcome);
        backend.Failure = "check";
        Assert.Equal(StartupUpdateOutcome.Blocked, (await StartupUpdateRunner.RunAsync(backend, _ => { }, default)).Outcome);
        Assert.Equal(new[] { "check", "check" }, backend.Calls);
    }
    [Fact]
    public async Task CancellationDuringNoUpdateResponseDoesNotAllowLogin()
    {
        using var cts = new CancellationTokenSource();
        var backend = new FakeBackend { Available = false, AfterCall = _ => cts.Cancel() };
        Assert.Equal(StartupUpdateOutcome.Canceled, (await StartupUpdateRunner.RunAsync(backend, _ => { }, cts.Token)).Outcome);
    }
    [Fact]
    public async Task UnexpectedCancellationWithoutUserCancelIsBlocked()
    {
        var backend = new FakeBackend { AfterCall = _ => throw new OperationCanceledException() };
        Assert.Equal(StartupUpdateOutcome.Blocked, (await StartupUpdateRunner.RunAsync(backend, _ => { }, default)).Outcome);
        Assert.DoesNotContain("apply", backend.Calls);
    }
    [Fact]
    public void ManagedSettingsAreMigratedOnceAndSurviveNewBundledDefaults()
    {
        WithDirectories((app, local) =>
        {
            var bundled = Path.Combine(app, "ninautoassist.config.json");
            ConfigLoader.SaveDefault(bundled);
            var storage = new UserSettingsStorage(app, local, true);
            storage.EnsureConfig();
            var original = File.ReadAllText(storage.ConfigPath);
            File.WriteAllText(bundled, "{}");
            storage.EnsureConfig();
            Assert.Equal(original, File.ReadAllText(storage.ConfigPath));
            Assert.StartsWith(local, storage.ConfigPath);
            Assert.Equal(Path.Combine(storage.DataDirectory, "logs/test.log"), storage.ResolveLogPath("logs/test.log"));
            Assert.Equal(bundled, new UserSettingsStorage(app, local, false).ConfigPath);
        });
    }
    [Fact]
    public void BrokenMigrationDoesNotCreatePersonalConfig()
    {
        WithDirectories((app, local) =>
        {
            File.WriteAllText(Path.Combine(app, "ninautoassist.config.json"), "not json");
            var storage = new UserSettingsStorage(app, local, true);
            Assert.ThrowsAny<Exception>(storage.EnsureConfig);
            Assert.False(File.Exists(storage.ConfigPath));
        });
    }
    private static void WithDirectories(Action<string, string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "NinAutoAssist-tests-" + Guid.NewGuid());
        try
        {
            var app = Path.Combine(root, "app"); Directory.CreateDirectory(app);
            test(app, Path.Combine(root, "local"));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class FakeBackend : IStartupUpdateBackend
    {
        public bool IsInstalled { get; init; } = true;
        public bool Available { get; init; } = true;
        public string? Failure { get; set; }
        public Action<string>? AfterCall { get; init; }
        public List<string> Calls { get; } = new();
        private void Call(string name)
        {
            Calls.Add(name); AfterCall?.Invoke(name);
            if (Failure == name) throw new Exception("secret");
        }
        public Task<bool> CheckAsync(CancellationToken cancellation) { Call("check"); return Task.FromResult(Available); }
        public Task DownloadAsync(Action<int> progress, CancellationToken cancellation) { Call("download"); progress(100); return Task.CompletedTask; }
        public void ApplyAndRestart() => Call("apply");
    }
}
