using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NinOnlineAutoAssist.Core.Security;

public sealed record FirebaseConfig
{
    public string ProjectId { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string DatabaseId { get; init; } = "(default)";
    public void Validate()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(ProjectId, "^[a-z][a-z0-9-]{4,62}$") ||
            string.IsNullOrWhiteSpace(ApiKey) || ApiKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_') ||
            (DatabaseId != "(default)" && !System.Text.RegularExpressions.Regex.IsMatch(DatabaseId, "^[a-z][a-z0-9-]{2,62}$")))
            throw new AccessDeniedException("Configuração Firebase inválida. Contate o administrador.");
    }
}

// Tokens exist only in process memory. Never serialize this type to logs or configuration.
public sealed class FirebaseIdentity(string uid, string email, string idToken, string refreshToken)
{
    public string Uid { get; } = uid;
    public string Email { get; } = email;
    internal string IdToken { get; } = idToken;
    internal string RefreshToken { get; } = refreshToken;
}

public sealed class FirebaseClient
{
    private readonly HttpClient _http;
    private readonly FirebaseConfig _config;
    private string Root => $"projects/{_config.ProjectId}/databases/{_config.DatabaseId}/documents";
    private string Documents => $"https://firestore.googleapis.com/v1/{Root}";
    public FirebaseClient(HttpClient http, FirebaseConfig config) { config.Validate(); _http = http; _config = config; }

    public async Task<FirebaseIdentity> SignInAsync(string email, string password, CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={_config.ApiKey}")
        { Content = JsonContent.Create(new { email, password, returnSecureToken = true }) };
        using var response = await SendAsync(request, cancellation).ConfigureAwait(false);
        using var json = await ParseAsync(response, cancellation).ConfigureAwait(false);
        return ParseIdentity(json.RootElement, false, null);
    }

    public async Task<FirebaseIdentity> RefreshAsync(FirebaseIdentity identity, CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://securetoken.googleapis.com/v1/token?key={_config.ApiKey}")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["grant_type"] = "refresh_token", ["refresh_token"] = identity.RefreshToken }) };
        using var response = await SendAsync(request, cancellation).ConfigureAwait(false);
        using var json = await ParseAsync(response, cancellation).ConfigureAwait(false);
        return ParseIdentity(json.RootElement, true, identity);
    }

    private static FirebaseIdentity ParseIdentity(JsonElement root, bool refresh, FirebaseIdentity? previous)
    {
        try
        {
            string Required(string key) => root.GetProperty(key).GetString() is { Length: > 0 } value ? value : throw new FormatException();
            var uid = Required(refresh ? "user_id" : "localId");
            if (previous is not null && uid != previous.Uid) throw new FormatException();
            if (!long.TryParse(Required(refresh ? "expires_in" : "expiresIn"), out var lifetime) || lifetime <= 0) throw new FormatException();
            return new(uid, refresh ? previous!.Email : Required("email"), Required(refresh ? "id_token" : "idToken"),
                Required(refresh ? "refresh_token" : "refreshToken"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException)
        { throw new AccessDeniedException("Resposta de autenticação inválida."); }
    }

    public async Task<(Entitlements Permissions, DateTimeOffset ServerTime)> ReadEntitlementsAsync(FirebaseIdentity identity, CancellationToken cancellation = default)
    {
        using var request = Authorized(HttpMethod.Get, $"{Documents}/users/{Uri.EscapeDataString(identity.Uid)}", identity);
        using var response = await SendAsync(request, cancellation).ConfigureAwait(false);
        using var json = await ParseAsync(response, cancellation).ConfigureAwait(false);
        var permissions = Entitlements.Parse(json.RootElement);
        var serverTime = response.Headers.Date;
        if (permissions.ExpiresAt is not null && serverTime is null) throw new AccessDeniedException("Horário do servidor indisponível; acesso recusado.");
        return (permissions, serverTime ?? DateTimeOffset.UtcNow);
    }

    public async Task WritePresenceAsync(FirebaseIdentity identity, string sessionId, string version, bool first, bool ended,
        CancellationToken cancellation = default)
    {
        if (FirebaseUsagePolicy.IsExempt(identity.Email)) return;
        if (!Guid.TryParseExact(sessionId, "N", out _)) throw new ArgumentException("Invalid session ID");
        object Text(string value) => new { stringValue = value };
        var fields = new Dictionary<string, object>();
        if (first)
        {
            fields["uid"] = Text(identity.Uid); fields["email"] = Text(identity.Email); fields["version"] = Text(version);
        }
        var timestamps = new List<string> { "lastSeenAt" };
        if (first) timestamps.Add("startedAt");
        if (ended) timestamps.Add("endedAt");
        var write = new Dictionary<string, object>
        {
            ["update"] = new { name = $"{Root}/appSessions/{sessionId}", fields },
            ["currentDocument"] = new { exists = !first },
            ["updateTransforms"] = timestamps.Select(field => new { fieldPath = field, setToServerValue = "REQUEST_TIME" }).ToArray()
        };
        if (!first) write["updateMask"] = new { fieldPaths = Array.Empty<string>() };
        using var request = Authorized(HttpMethod.Post, $"{Documents}:commit", identity);
        request.Content = JsonContent.Create(new { writes = new[] { write } });
        try { using var response = await SendAsync(request, cancellation).ConfigureAwait(false); }
        catch (Exception ex) when (first && (ex is AccessDeniedException or FirebaseUnavailableException))
        {
            if (await PresenceCreatedAsync(identity, sessionId, version, cancellation).ConfigureAwait(false)) return;
            throw;
        }
    }

    private async Task<bool> PresenceCreatedAsync(FirebaseIdentity identity, string sessionId, string version, CancellationToken cancellation)
    {
        try
        {
            using var request = Authorized(HttpMethod.Get, $"{Documents}/appSessions/{sessionId}", identity);
            using var response = await SendAsync(request, cancellation).ConfigureAwait(false);
            using var json = await ParseAsync(response, cancellation).ConfigureAwait(false);
            var fields = json.RootElement.GetProperty("fields");
            string? Text(string key) => fields.GetProperty(key).GetProperty("stringValue").GetString();
            return Text("uid") == identity.Uid && Text("email") == identity.Email && Text("version") == version &&
                !fields.TryGetProperty("endedAt", out _) &&
                fields.GetProperty("startedAt").GetProperty("timestampValue").GetDateTimeOffset() != default &&
                fields.GetProperty("lastSeenAt").GetProperty("timestampValue").GetDateTimeOffset() != default;
        }
        catch (Exception ex) when (ex is AccessDeniedException or FirebaseUnavailableException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }

    public async Task WriteCharacterChangeAsync(FirebaseIdentity identity, string sessionId, CharacterUsageChange change,
        CancellationToken cancellation = default)
    {
        if (FirebaseUsagePolicy.IsExempt(identity.Email)) return;
        if (!Guid.TryParseExact(sessionId, "N", out _)) throw new ArgumentException("Invalid session ID");
        change.Validate();
        var eventId = change.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var eventPath = $"{Root}/appSessions/{sessionId}/events/{eventId}";
        object Text(string value) => new { stringValue = value };
        object Integer(long value) => new { integerValue = value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        object Transform(string field) => new { fieldPath = field, setToServerValue = "REQUEST_TIME" };
        var sessionWrite = new
        {
            update = new { name = $"{Root}/appSessions/{sessionId}", fields = new Dictionary<string, object>
                { ["characterName"] = Text(change.CharacterName), ["characterSequence"] = Integer(change.Sequence) } },
            updateMask = new { fieldPaths = new[] { "characterName", "characterSequence" } },
            currentDocument = new { exists = true },
            updateTransforms = new[] { Transform("characterChangedAt"), Transform("lastSeenAt") }
        };
        var eventWrite = new
        {
            update = new { name = eventPath, fields = new Dictionary<string, object>
            {
                ["uid"] = Text(identity.Uid), ["email"] = Text(identity.Email), ["kind"] = Text("characterChanged"),
                ["previousCharacterName"] = Text(change.PreviousCharacterName), ["characterName"] = Text(change.CharacterName),
                ["sequence"] = Integer(change.Sequence)
            } },
            currentDocument = new { exists = false },
            updateTransforms = new[] { Transform("recordedAt") }
        };
        try
        {
            using var request = Authorized(HttpMethod.Post, $"{Documents}:commit", identity);
            request.Content = JsonContent.Create(new { writes = new object[] { sessionWrite, eventWrite } });
            using var response = await SendAsync(request, cancellation).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is AccessDeniedException or FirebaseUnavailableException)
        {
            // A committed response can be lost. Deterministic immutable event IDs let a retry verify
            // the exact same event instead of inventing a duplicate or overwriting later history.
            if (await CharacterChangeRecordedAsync(identity, eventPath, change, cancellation).ConfigureAwait(false)) return;
            throw;
        }
    }

    private async Task<bool> CharacterChangeRecordedAsync(FirebaseIdentity identity, string eventPath, CharacterUsageChange change,
        CancellationToken cancellation)
    {
        try
        {
            using var request = Authorized(HttpMethod.Get, $"https://firestore.googleapis.com/v1/{eventPath}", identity);
            using var response = await SendAsync(request, cancellation).ConfigureAwait(false);
            using var json = await ParseAsync(response, cancellation).ConfigureAwait(false);
            var fields = json.RootElement.GetProperty("fields");
            string? Text(string key) => fields.GetProperty(key).GetProperty("stringValue").GetString();
            return Text("uid") == identity.Uid && Text("email") == identity.Email && Text("kind") == "characterChanged" &&
                Text("previousCharacterName") == change.PreviousCharacterName && Text("characterName") == change.CharacterName &&
                fields.GetProperty("sequence").GetProperty("integerValue").GetString() == change.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                fields.GetProperty("recordedAt").GetProperty("timestampValue").GetDateTimeOffset() != default;
        }
        catch (Exception ex) when (ex is AccessDeniedException or FirebaseUnavailableException or JsonException or
            KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, FirebaseIdentity identity)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity.IdToken);
        return request;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            var response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            response.Dispose();
            // Never echo Firebase response bodies, credentials, tokens or request URLs into errors.
            if (status == HttpStatusCode.TooManyRequests) throw new AccessDeniedException("Muitas tentativas. Aguarde e tente novamente.");
            if ((int)status >= 500) throw new FirebaseUnavailableException();
            throw new AccessDeniedException("Login ou acesso recusado. Confira a conta e as permissões com o administrador.");
        }
        catch (HttpRequestException) { throw new FirebaseUnavailableException(); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new FirebaseUnavailableException(); }
    }
    private static async Task<JsonDocument> ParseAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)); }
        catch (JsonException) { throw new AccessDeniedException("Resposta inválida do Firebase."); }
    }
}
