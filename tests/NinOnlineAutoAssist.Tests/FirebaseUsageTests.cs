using System.Net;
using System.Text;
using System.Text.Json;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class FirebaseUsageTests
{
    private sealed class Server(string email) : HttpMessageHandler
    {
        public int AuthReads, PermissionReads, Refreshes, PresenceWrites;
        public bool FailChanges, CommitButLoseResponse, LoseFirstPresenceResponse, CorruptStoredEvent;
        public readonly List<JsonElement> ChangeBatches = new();
        public readonly Dictionary<string, string> Events = new();
        public readonly Dictionary<string, string> Sessions = new();
        public FirebaseClient Client(HttpClient http) => new(http, new FirebaseConfig { ProjectId = "test-project", ApiKey = "public-key" });
        private static HttpResponseMessage Response(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            response.Headers.Date = new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero); return response;
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("accounts:signInWithPassword"))
            {
                AuthReads++;
                return Response(JsonSerializer.Serialize(new { localId = "uid-one", email, idToken = "id-token", refreshToken = "refresh-token", expiresIn = "3600" }));
            }
            if (request.RequestUri.Host == "securetoken.googleapis.com")
            {
                Refreshes++;
                return Response("""{"user_id":"uid-one","id_token":"new-id","refresh_token":"new-refresh","expires_in":"3600"}""");
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/users/uid-one"))
            {
                PermissionReads++;
                return Response("""{"fields":{"enabled":{"booleanValue":true},"features":{"mapValue":{}}}}""");
            }
            if (request.Method == HttpMethod.Get && path.Contains("/events/"))
                return Events.TryGetValue(path[4..], out var existing) ? Response(existing) : Response("{}", HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get && path.Contains("/appSessions/"))
                return Sessions.TryGetValue(path[4..], out var storedSession) ? Response(storedSession) : Response("{}", HttpStatusCode.NotFound);
            Assert.Equal(HttpMethod.Post, request.Method); Assert.EndsWith("documents:commit", path);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var writes = json.RootElement.GetProperty("writes");
            if (writes.GetArrayLength() == 1)
            {
                PresenceWrites++;
                var first = !writes[0].GetProperty("currentDocument").GetProperty("exists").GetBoolean();
                if (first)
                {
                    var updateSession = writes[0].GetProperty("update");
                    var sessionFields = updateSession.GetProperty("fields").EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
                    sessionFields["startedAt"] = sessionFields["lastSeenAt"] = new { timestampValue = "2026-11-04T00:00:00Z" };
                    Sessions[updateSession.GetProperty("name").GetString()!] = JsonSerializer.Serialize(new { fields = sessionFields });
                    if (LoseFirstPresenceResponse) { LoseFirstPresenceResponse = false; return Response("{}", HttpStatusCode.ServiceUnavailable); }
                }
                return Response("{}");
            }
            Assert.Equal(2, writes.GetArrayLength());
            ChangeBatches.Add(json.RootElement.Clone());
            if (FailChanges) return Response("{}", HttpStatusCode.ServiceUnavailable);
            var update = writes[1].GetProperty("update");
            var fields = update.GetProperty("fields").EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone());
            fields["recordedAt"] = new { timestampValue = "2026-11-04T00:00:00Z" };
            if (CorruptStoredEvent) fields["characterName"] = new { stringValue = "Mismatched" };
            Events[update.GetProperty("name").GetString()!] = JsonSerializer.Serialize(new { fields });
            if (CommitButLoseResponse) { CommitButLoseResponse = false; return Response("{}", HttpStatusCode.ServiceUnavailable); }
            return Response("{}");
        }
    }

    private static CharacterUsageChange Change(JsonElement batch)
    {
        var fields = batch.GetProperty("writes")[1].GetProperty("update").GetProperty("fields");
        return new(long.Parse(fields.GetProperty("sequence").GetProperty("integerValue").GetString()!),
            fields.GetProperty("previousCharacterName").GetProperty("stringValue").GetString()!,
            fields.GetProperty("characterName").GetProperty("stringValue").GetString()!);
    }

    [Theory]
    [InlineData("pain@ninassist.com.br")]
    [InlineData("PAIN@NINASSIST.COM.BR")]
    public async Task ExemptAccountStillAuthenticatesAndRefreshesButNeverWritesAnyUsage(string email)
    {
        using var server = new Server(email); using var http = new HttpClient(server);
        var session = await FirebaseSession.LoginAsync(server.Client(http), "editable-other@example.test", "password", new SimulatedClock(0), "1.0");
        try
        {
            Assert.True(session.UsageExempt); Assert.True(session.Access.IsValid);
            Assert.False(session.ObserveCharacter("TestCharacterOne"));
            await session.FlushCharacterUsageAsync(); await session.ValidateAsync();
            Assert.Contains("dispensados", session.Status); Assert.Equal(0, session.PendingCharacterChanges);
        }
        finally { await session.DisposeAsync(); }
        Assert.Equal(1, server.AuthReads); Assert.Equal(1, server.Refreshes); Assert.Equal(2, server.PermissionReads);
        Assert.Equal(0, server.PresenceWrites); Assert.Empty(server.ChangeBatches); Assert.Empty(server.Events);
    }

    [Fact]
    public async Task LowLevelWriterAlsoSuppressesExemptUsageWithoutNetworkRequests()
    {
        using var server = new Server("pain@ninassist.com.br"); using var http = new HttpClient(server);
        var client = server.Client(http);
        var identity = new FirebaseIdentity("admin-uid", "pain@ninassist.com.br", "token", "refresh");
        var id = Guid.NewGuid().ToString("N");
        await client.WritePresenceAsync(identity, id, "1.0", true, false);
        await client.WritePresenceAsync(identity, id, "1.0", false, false);
        await client.WritePresenceAsync(identity, id, "1.0", false, true);
        await client.WriteCharacterChangeAsync(identity, id, new(1, "", "TestCharacterOne"));
        Assert.Equal(0, server.PresenceWrites); Assert.Empty(server.ChangeBatches); Assert.Equal(0, server.AuthReads);
    }

    [Theory]
    [InlineData("other@example.test")]
    [InlineData("pain+alias@ninassist.com.br")]
    [InlineData("notpain@ninassist.com.br")]
    [InlineData("pain@ninassist.com.br.example.test")]
    public async Task OtherAccountsCannotClaimExemptionByTypingAdminEmail(string actualEmail)
    {
        using var server = new Server(actualEmail); using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "pain@ninassist.com.br", "password", new SimulatedClock(0), "1.0");
        Assert.False(session.UsageExempt); Assert.Equal(1, server.PresenceWrites);
        Assert.True(session.ObserveCharacter("Player Name")); await session.FlushCharacterUsageAsync();
        Assert.Equal(actualEmail, server.ChangeBatches[0].GetProperty("writes")[1].GetProperty("update")
            .GetProperty("fields").GetProperty("email").GetProperty("stringValue").GetString());
    }

    [Fact]
    public async Task RepeatedObservationsAreDeduplicatedButEveryActualChangeIncludingReturnIsPreserved()
    {
        using var server = new Server("user@example.test"); using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        Assert.True(session.ObserveCharacter("TestCharacterTwo")); Assert.False(session.ObserveCharacter("TestCharacterTwo"));
        Assert.True(session.ObserveCharacter("TestCharacterOne")); Assert.True(session.ObserveCharacter("TestCharacterTwo"));
        Assert.Empty(server.ChangeBatches); Assert.Equal(3, session.PendingCharacterChanges); // UI does no HTTP.
        await session.FlushCharacterUsageAsync();
        Assert.Equal(new[] { new CharacterUsageChange(1, "", "TestCharacterTwo"),
            new CharacterUsageChange(2, "TestCharacterTwo", "TestCharacterOne"), new CharacterUsageChange(3, "TestCharacterOne", "TestCharacterTwo") },
            server.ChangeBatches.Select(Change));
        Assert.Equal(0, session.PendingCharacterChanges); Assert.Equal(3, server.Events.Count);
        Assert.False(session.ObserveCharacter("TestCharacterTwo")); await session.FlushCharacterUsageAsync(); Assert.Equal(3, server.Events.Count);
    }

    [Fact]
    public async Task FailureRetainsOrderedHistoryAndRetryReusesSameEventId()
    {
        using var server = new Server("user@example.test") { FailChanges = true }; using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        session.ObserveCharacter("First"); session.ObserveCharacter("Second");
        await session.FlushCharacterUsageAsync();
        Assert.True(session.Access.IsValid); Assert.Equal(2, session.PendingCharacterChanges); Assert.Contains("2 alterações pendentes", session.Status);
        server.FailChanges = false; await session.FlushCharacterUsageAsync();
        Assert.Equal(new long[] { 1, 1, 2 }, server.ChangeBatches.Select(b => Change(b).Sequence));
        Assert.Equal(2, server.Events.Count); Assert.Equal(0, session.PendingCharacterChanges);
    }

    [Fact]
    public async Task LostCommitResponseIsVerifiedAsExistingInsteadOfDuplicatingHistory()
    {
        using var server = new Server("user@example.test") { CommitButLoseResponse = true }; using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        session.ObserveCharacter("First"); session.ObserveCharacter("Second"); await session.FlushCharacterUsageAsync();
        Assert.Equal(2, server.ChangeBatches.Count); Assert.Equal(2, server.Events.Count); Assert.Equal(0, session.PendingCharacterChanges);
    }

    [Fact]
    public async Task LostInitialPresenceResponseCanBeRecoveredWithoutCreatingAnotherSession()
    {
        using var server = new Server("user@example.test") { LoseFirstPresenceResponse = true }; using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        session.ObserveCharacter("Player"); await session.FlushCharacterUsageAsync();
        Assert.Equal(1, server.PresenceWrites); Assert.Single(server.Sessions); Assert.Single(server.Events); Assert.Equal(0, session.PendingCharacterChanges);
    }

    [Fact]
    public async Task MismatchedExistingEventCannotBeMistakenForSuccessfulDelivery()
    {
        using var server = new Server("user@example.test") { CommitButLoseResponse = true, CorruptStoredEvent = true }; using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        session.ObserveCharacter("Player"); await session.FlushCharacterUsageAsync();
        Assert.Equal(1, session.PendingCharacterChanges); Assert.Contains("pendentes", session.Status); Assert.True(session.Access.IsValid);
    }

    [Fact]
    public async Task SessionSummaryAndImmutableEventAreWrittenTogetherWithServerTimestamps()
    {
        using var server = new Server("user@example.test"); using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        session.ObserveCharacter("Player Name"); await session.FlushCharacterUsageAsync();
        var writes = Assert.Single(server.ChangeBatches).GetProperty("writes");
        var summary = writes[0]; var history = writes[1];
        Assert.Equal(new[] { "characterName", "characterSequence" }, summary.GetProperty("updateMask").GetProperty("fieldPaths").EnumerateArray().Select(x => x.GetString()));
        Assert.True(summary.GetProperty("currentDocument").GetProperty("exists").GetBoolean());
        Assert.False(history.GetProperty("currentDocument").GetProperty("exists").GetBoolean());
        Assert.EndsWith("/events/1", history.GetProperty("update").GetProperty("name").GetString());
        Assert.Equal(new[] { "email", "kind", "previousCharacterName", "characterName", "sequence", "uid" }.Order(),
            history.GetProperty("update").GetProperty("fields").EnumerateObject().Select(p => p.Name).Order());
        foreach (var write in writes.EnumerateArray())
            foreach (var transform in write.GetProperty("updateTransforms").EnumerateArray())
                Assert.Equal("REQUEST_TIME", transform.GetProperty("setToServerValue").GetString());
    }

    [Fact]
    public async Task LogoutFlushesPendingCharacterBeforeEndAndRevocationPreventsNewObservations()
    {
        using var server = new Server("user@example.test"); using var http = new HttpClient(server);
        var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        session.ObserveCharacter("Player"); session.Revoke(); Assert.False(session.ObserveCharacter("Other"));
        await session.DisposeAsync();
        Assert.Equal("Player", Change(Assert.Single(server.ChangeBatches)).CharacterName); Assert.Equal(2, server.PresenceWrites);
    }

    [Theory]
    [InlineData("")] [InlineData("   ")] [InlineData("Player\nName")]
    public async Task InvalidNamesAreNotQueuedOrTransmitted(string name)
    {
        using var server = new Server("user@example.test"); using var http = new HttpClient(server);
        await using var session = await FirebaseSession.LoginAsync(server.Client(http), "user@example.test", "password", new SimulatedClock(0), "1.0");
        Assert.False(session.ObserveCharacter(name)); Assert.False(session.ObserveCharacter(new string('x', 65)));
        await session.FlushCharacterUsageAsync(); Assert.Empty(server.ChangeBatches);
    }
}
