using System.Net;
using System.Text;
using System.Text.Json;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class FirebaseSecurityTests
{
    private const string Document = """{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{"fields":{"Tool":{"booleanValue":true},"AutoAttack":{"booleanValue":false}}}}}}""";
    private const string Auth = """{"localId":"uid-one","email":"user@example.test","idToken":"secret-id","refreshToken":"secret-refresh","expiresIn":"3600"}""";
    private const string Refresh = """{"user_id":"uid-one","id_token":"new-secret-id","refresh_token":"new-secret-refresh","expires_in":"3600","project_id":"737849383836"}""";
    private static readonly DateTimeOffset ServerTime = new(2026, 11, 3, 20, 0, 0, TimeSpan.Zero);
    private static FirebaseConfig Config => new() { ProjectId = "test-project", ApiKey = "public-key" };
    private static Entitlements Parse(string json)
    { using var document = JsonDocument.Parse(json); return Entitlements.Parse(document.RootElement); }
    private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        response.Headers.Date = ServerTime; return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    [Fact]
    public void LeaseStartsDeniedAndMissingPermissionsAreDenied()
    {
        var clock = new SimulatedClock(0); var access = new AccessLease(clock);
        Assert.False(access.IsValid);
        foreach (var feature in Enum.GetValues<AppFeature>()) Assert.False(access.Allows(feature));
        access.Apply(Parse(Document), 0, ServerTime);
        Assert.True(access.IsValid); Assert.True(access.Allows(AppFeature.Tool));
        foreach (var feature in Enum.GetValues<AppFeature>().Where(f => f != AppFeature.Tool)) Assert.False(access.Allows(feature));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"fields":{"enabled":{"booleanValue":false},"features":{"mapValue":{}}}}""")]
    [InlineData("""{"fields":{"enabled":{"stringValue":"true"},"features":{"mapValue":{}}}}""")]
    [InlineData("""{"fields":{"enabled":{"booleanValue":true}}}""")]
    [InlineData("""{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{"fields":{"Tool":{"stringValue":"true"}}}}}}""")]
    [InlineData("""{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{}},"expiresAt":{"nullValue":null}}}""")]
    [InlineData("""{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{}},"expiresAt":{"timestampValue":"bad"}}}""")]
    public void MissingDisabledOrMalformedDocumentsFailClosed(string json) => Assert.Throws<AccessDeniedException>(() => Parse(json));

    [Fact]
    public void EmptyMapAndUnknownFeatureNeverGrantAnything()
    {
        foreach (var json in new[] { """{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{}}}}""",
            """{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{"fields":{"tool":{"booleanValue":true},"999":{"booleanValue":true},"All":{"booleanValue":true}}}}}}""" })
        {
            var permissions = Parse(json);
            foreach (var feature in Enum.GetValues<AppFeature>()) Assert.False(permissions.Allows(feature));
        }
    }

    [Fact]
    public void LeaseExpiresMonotonicallyAndSlowRequestsDoNotExtendIt()
    {
        var clock = new SimulatedClock(0); var access = new AccessLease(clock);
        clock.Advance(10_000); access.Apply(Parse(Document), 0, ServerTime);
        clock.Advance(64_999); Assert.True(access.IsValid);
        clock.Advance(1); Assert.False(access.IsValid); Assert.False(access.Allows(AppFeature.Tool));
        Assert.Throws<AccessDeniedException>(() => access.Apply(Parse(Document), 0, ServerTime));
        Assert.False(access.IsValid);
    }

    [Fact]
    public void ExpiryUsesServerTimeAndBoundsMonotonicLease()
    {
        var clock = new SimulatedClock(0); var access = new AccessLease(clock);
        access.Apply(new([AppFeature.Follow], ServerTime.AddSeconds(10)), 0, ServerTime);
        clock.Advance(7999); Assert.True(access.Allows(AppFeature.Follow));
        clock.Advance(1); Assert.False(access.IsValid);
        Assert.Throws<AccessDeniedException>(() => access.Apply(new([AppFeature.Tool], ServerTime), clock.NowMs, ServerTime));
    }

    [Fact]
    public void RevocationAndReplacementDoNotMergeOldPermissionsAndCloseCannotBeUndone()
    {
        var access = new AccessLease(new SimulatedClock(0));
        access.Apply(new([AppFeature.SubPunishLegacy, AppFeature.AutoAttack]), 0, ServerTime);
        Assert.False(access.Allows(AppFeature.SubPunishNew)); Assert.False(access.Allows(AppFeature.Tool));
        access.Apply(new([AppFeature.Tool]), 0, ServerTime);
        Assert.False(access.Allows(AppFeature.AutoAttack)); Assert.True(access.Allows(AppFeature.Tool));
        access.Revoke(); Assert.False(access.IsValid);
        access.Close(); Assert.Throws<AccessDeniedException>(() => access.Apply(new([AppFeature.Tool]), 0, ServerTime));
    }

    [Theory]
    [InlineData(true, false, CombatAction.Attack)]
    [InlineData(false, true, CombatAction.Tool)]
    public void IndependentCombatGatesCannotExecuteOtherAction(bool attack, bool tool, CombatAction expected)
    {
        var clock = new SimulatedClock(0); var calls = new List<CombatAction>();
        var engine = new AutoAttackToolEngine(clock, new CombatAssistSettings { WeaponRadius = 2, ToolRange = 3 },
            (action, _) => { calls.Add(action); return ActionResult.Ok(); });
        var frame = new LiveFrame(new GameSnapshot { Player = new PlayerState { Id = "local", Facing = Direction.Right },
            Target = new TargetInfo { Id = "target", IsTargeted = true, PositionX = 1 } }, 0, 51, "local", "target", false, null, 0);
        engine.Update(frame, attack, tool, false); Assert.Equal(new[] { expected }, calls);
        engine.Update(frame, false, false, false); Assert.Single(calls);
    }

    [Fact]
    public async Task RestUsesCorrectEndpointsRefreshFieldsAndBearerHeader()
    {
        var step = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            switch (step++)
            {
                case 0:
                    Assert.Equal("identitytoolkit.googleapis.com", request.RequestUri!.Host);
                    Assert.Contains("accounts:signInWithPassword", request.RequestUri.AbsolutePath);
                    Assert.Contains("password", body); Assert.Contains("returnSecureToken", body); return Response(Auth);
                case 1:
                    Assert.Equal("securetoken.googleapis.com", request.RequestUri!.Host);
                    Assert.Contains("grant_type=refresh_token", body); Assert.Contains("refresh_token=secret-refresh", body); return Response(Refresh);
                default:
                    Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                    Assert.Equal("new-secret-id", request.Headers.Authorization.Parameter);
                    Assert.EndsWith("/users/uid-one", request.RequestUri!.AbsolutePath);
                    Assert.DoesNotContain("secret", request.RequestUri.ToString()); return Response(Document);
            }
        }));
        var client = new FirebaseClient(http, Config);
        var identity = await client.SignInAsync("user@example.test", "not-persisted");
        identity = await client.RefreshAsync(identity);
        var permissions = await client.ReadEntitlementsAsync(identity);
        Assert.True(permissions.Permissions.Allows(AppFeature.Tool)); Assert.Equal(ServerTime, permissions.ServerTime);
        Assert.DoesNotContain("secret", identity.ToString()); Assert.Equal(3, step);
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(500)]
    public async Task ErrorMessagesNeverEchoSecretsOrFirebaseResponse(int status)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Response("password secret-id secret-refresh", (HttpStatusCode)status))));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => new FirebaseClient(http, Config).SignInAsync("private-email", "private-password"));
        Assert.DoesNotContain("secret", error.ToString()); Assert.DoesNotContain("private", error.ToString());
        Assert.DoesNotContain("public-key", error.ToString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("""{"localId":"uid-one","email":"user@example.test","idToken":"secret","refreshToken":"secret","expiresIn":"0"}""")]
    public async Task MalformedAuthCannotOpenASession(string auth)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Response(auth))));
        await Assert.ThrowsAsync<AccessDeniedException>(() => new FirebaseClient(http, Config).SignInAsync("email", "password"));
    }

    [Fact]
    public async Task RefreshCannotSwitchUserIdentity()
    {
        var step = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Response(step++ == 0 ? Auth : Refresh.Replace("uid-one", "other-uid")))));
        var client = new FirebaseClient(http, Config); var identity = await client.SignInAsync("email", "password");
        await Assert.ThrowsAsync<AccessDeniedException>(() => client.RefreshAsync(identity));
    }

    [Fact]
    public async Task PresenceUsesOnlyAccountVersionAndServerTimestamps()
    {
        var step = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (step++ == 0) return Response(Auth);
            Assert.EndsWith("documents:commit", request.RequestUri!.AbsolutePath);
            Assert.Equal("secret-id", request.Headers.Authorization!.Parameter);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var write = json.RootElement.GetProperty("writes")[0];
            var update = write.GetProperty("update");
            var fields = update.GetProperty("fields");
            if (step == 2)
            {
                Assert.Equal(new[] { "email", "uid", "version" }, fields.EnumerateObject().Select(p => p.Name).Order().ToArray());
                Assert.False(write.GetProperty("currentDocument").GetProperty("exists").GetBoolean());
                Assert.Equal(new[] { "lastSeenAt", "startedAt" }, write.GetProperty("updateTransforms").EnumerateArray().Select(t => t.GetProperty("fieldPath").GetString()).ToArray());
            }
            else
            {
                Assert.Empty(fields.EnumerateObject());
                Assert.Empty(write.GetProperty("updateMask").GetProperty("fieldPaths").EnumerateArray());
                Assert.True(write.GetProperty("currentDocument").GetProperty("exists").GetBoolean());
                Assert.Equal(step == 4 ? new[] { "lastSeenAt", "endedAt" } : new[] { "lastSeenAt" },
                    write.GetProperty("updateTransforms").EnumerateArray().Select(t => t.GetProperty("fieldPath").GetString()).ToArray());
            }
            foreach (var transform in write.GetProperty("updateTransforms").EnumerateArray())
                Assert.Equal("REQUEST_TIME", transform.GetProperty("setToServerValue").GetString());
            return Response("{}");
        }));
        var client = new FirebaseClient(http, Config); var identity = await client.SignInAsync("email", "password");
        var id = Guid.NewGuid().ToString("N");
        await client.WritePresenceAsync(identity, id, "1.0", true, false);
        await client.WritePresenceAsync(identity, id, "1.0", false, false);
        await client.WritePresenceAsync(identity, id, "1.0", false, true);
        Assert.Equal(4, step);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task DisabledAccountOrNetworkFailureDuringRevalidationRevokesLease(bool disabled)
    {
        var step = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            return Task.FromResult(step++ switch
            {
                0 => Response(Auth), 1 => Response(Document), 2 => Response("{}"), 3 => Response(Refresh),
                _ => disabled ? Response(Document.Replace("\"enabled\":{\"booleanValue\":true}", "\"enabled\":{\"booleanValue\":false}"))
                    : throw new HttpRequestException("secret network payload")
            });
        }));
        await using var session = await FirebaseSession.LoginAsync(new(http, Config), "email", "password", new SimulatedClock(0), "1.0");
        Assert.True(session.Access.IsValid);
        await Assert.ThrowsAnyAsync<Exception>(() => session.ValidateAsync());
        Assert.False(session.Access.IsValid); Assert.False(session.Access.Allows(AppFeature.Tool));
    }

    [Fact]
    public async Task PresenceFailureIsVisibleAndDoesNotInventSuccessfulRecording()
    {
        var step = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(step++ switch
        { 0 => Response(Auth), 1 => Response(Document), _ => Response("{}", HttpStatusCode.Forbidden) })));
        await using var session = await FirebaseSession.LoginAsync(new(http, Config), "email", "password", new SimulatedClock(0), "1.0");
        Assert.True(session.Access.Allows(AppFeature.Tool)); Assert.Contains("presença indisponível", session.Status);
        session.Revoke(); Assert.False(session.Access.IsValid);
        await Assert.ThrowsAsync<AccessDeniedException>(() => session.ValidateAsync());
    }

    [Fact]
    public async Task LateEntitlementsResponseCannotUndoLogout()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var step = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            switch (Interlocked.Increment(ref step))
            {
                case 1: return Task.FromResult(Response(Auth));
                case 2: return Task.FromResult(Response(Document));
                case 3: return Task.FromResult(Response("{}"));
                case 4: return Task.FromResult(Response(Refresh));
                case 5: reached.SetResult(); return pending.Task;
                default: return Task.FromResult(Response("{}"));
            }
        }));
        await using var session = await FirebaseSession.LoginAsync(new(http, Config), "email", "password", new SimulatedClock(0), "1.0");
        var validation = session.ValidateAsync();
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(3));
        session.Revoke(); pending.SetResult(Response(Document));
        await Assert.ThrowsAsync<AccessDeniedException>(() => validation);
        Assert.False(session.Access.IsValid);
    }

    [Theory]
    [InlineData(404)] [InlineData(403)]
    public async Task MissingOrUnreadableUserDocumentCannotStartSession(int status)
    {
        var step = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(step++ == 0 ? Response(Auth) : Response("{}", (HttpStatusCode)status))));
        await Assert.ThrowsAsync<AccessDeniedException>(() => FirebaseSession.LoginAsync(new(http, Config), "email", "password", new SimulatedClock(0), "1.0"));
        Assert.Equal(2, step); // No presence or automation session created.
    }

    [Fact]
    public async Task TokenRevocationDuringRefreshDeniesAllFeatures()
    {
        var step = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(step++ switch
        { 0 => Response(Auth), 1 => Response(Document), 2 => Response("{}"), _ => Response("revoked secret-refresh", HttpStatusCode.BadRequest) })));
        await using var session = await FirebaseSession.LoginAsync(new(http, Config), "email", "password", new SimulatedClock(0), "1.0");
        await Assert.ThrowsAsync<AccessDeniedException>(() => session.ValidateAsync());
        foreach (var feature in Enum.GetValues<AppFeature>()) Assert.False(session.Access.Allows(feature));
    }

    [Fact]
    public async Task ExpiringDocumentRequiresServerClockRatherThanTrustingLocalClock()
    {
        using var http = new HttpClient(new Handler(_ =>
        {
            var response = Response("""{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{}},"expiresAt":{"timestampValue":"2026-11-03T20:01:00Z"}}}""");
            response.Headers.Date = null; return Task.FromResult(response);
        }));
        await Assert.ThrowsAsync<AccessDeniedException>(() => new FirebaseClient(http, Config)
            .ReadEntitlementsAsync(new FirebaseIdentity("uid", "email", "token", "refresh")));
    }

    [Fact]
    public async Task StalledRevalidationCannotKeepHeldFeaturesAuthorizedBeyondLease()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new SimulatedClock(0); var step = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            switch (++step)
            {
                case 1: return Task.FromResult(Response(Auth));
                case 2: return Task.FromResult(Response(Document));
                case 3: return Task.FromResult(Response("{}"));
                case 4: return Task.FromResult(Response(Refresh));
                case 5: reached.SetResult(); return pending.Task;
                default: return Task.FromResult(Response("{}"));
            }
        }));
        await using var session = await FirebaseSession.LoginAsync(new(http, Config), "email", "password", clock, "1.0");
        var validation = session.ValidateAsync(); await reached.Task.WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(75_000); Assert.False(session.Access.IsValid);
        pending.SetResult(Response(Document));
        await Assert.ThrowsAsync<AccessDeniedException>(() => validation);
        Assert.False(session.Access.IsValid);
    }
}
