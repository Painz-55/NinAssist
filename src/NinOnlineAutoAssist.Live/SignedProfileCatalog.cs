using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NinOnlineAutoAssist.Live;

/// <summary>Data-only compatibility feed. A signed mapping never bypasses live read invariants.</summary>
public static class SignedProfileCatalog
{
    public const string FeedUrl = "https://raw.githubusercontent.com/Painz-55/NinAssist/main/compatibility/profiles.json";
    public const string PublicKey = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqyxEIXbJaWxmyaldzpR94Rf4uURa
        11hsxWnZljnORHzLXlTGn4xaM1OtGFgCFC5Tim6+U0/y4T/cXTefRhuEiA==
        -----END PUBLIC KEY-----
        """;

    private static readonly string[] RequiredNames =
    [
        "Id", "Sha256", "CommonSha256", "Roots", "Globals", "Players", "LocalIndex", "SelectedIndex",
        "Name", "Village", "Map", "PixelX", "PixelY", "Facing", "Vitals", "MaxVitals", "Running",
        "Buffs", "BuffActive", "BuffName", "BuffDuration", "MapRoot", "MapIdentity", "Tiles", "Projectiles"
    ];
    private static readonly string[] CastNames = ["GlobalSpell", "GlobalStart", "GameClock", "PlayerSpell", "PlayerEnd"];
    private static readonly string[] TargetNames = ["GlobalObject", "Kind", "Index"];
    private static readonly Regex FieldName = new("^[A-Za-z_][A-Za-z0-9_`.+]{0,200}$", RegexOptions.CultureInvariant);

    public static GameMemoryProfile? VerifyAndSelect(byte[] payload, byte[] signature, string publicKey,
        string gameHash, string commonHash)
    {
        if (payload.Length is < 2 or > 65536 || signature.Length is < 64 or > 256) throw new InvalidOperationException("Invalid profile feed size");
        using var key = ECDsa.Create();
        key.ImportFromPem(publicKey);
        if (!key.VerifyData(payload, signature, HashAlgorithmName.SHA256)) throw new CryptographicException("Profile feed signature invalid");
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 10 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.GetProperty("schema").GetInt32() != 1)
            throw new InvalidOperationException("Unsupported profile schema");
        var profiles = root.GetProperty("profiles");
        if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() > 32)
            throw new InvalidOperationException("Invalid profile list");
        GameMemoryProfile? match = null;
        foreach (var element in profiles.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Invalid profile");
            var candidateGame = element.GetProperty("Sha256").GetString();
            var candidateCommon = element.GetProperty("CommonSha256").GetString();
            if (!IsHash(candidateGame) || !IsHash(candidateCommon)) throw new InvalidOperationException("Invalid fingerprint");
            if (!string.Equals(candidateGame, gameHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(candidateCommon, commonHash, StringComparison.OrdinalIgnoreCase)) continue;
            if (match is not null) throw new InvalidOperationException("Ambiguous signed profile");
            foreach (var name in RequiredNames)
            {
                var value = element.GetProperty(name).GetString();
                if (string.IsNullOrWhiteSpace(value) || value.Length > 200 ||
                    (name is not ("Id" or "Sha256" or "CommonSha256") && !FieldName.IsMatch(value)))
                    throw new InvalidOperationException($"Invalid profile field {name}");
            }
            var cast = element.GetProperty("CastTiming");
            if (cast.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Missing cast mapping");
            foreach (var name in CastNames) RequireField(cast, name);
            var selected = element.GetProperty("SelectedTarget");
            if (selected.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in TargetNames) RequireField(selected, name);
                if (selected.GetProperty("PlayerKind").GetInt32() is < 0 or > 16) throw new InvalidOperationException("Invalid target kind");
            }
            else if (selected.ValueKind != JsonValueKind.Null) throw new InvalidOperationException("Invalid target schema");
            // A publisher must explicitly mark a passively validated mapping as safe for keyboard dispatch.
            _ = element.GetProperty("ActuationValidated").GetBoolean();
            _ = element.GetProperty("MapIdentityInRoots").GetBoolean();
            var actions = element.GetProperty("ValidatedInputKinds");
            if (actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() > 7 ||
                actions.EnumerateArray().Any(a => a.ValueKind != JsonValueKind.String ||
                    a.GetString() is not ("Turning" or "Punish" or "Attack" or "Tool" or "Follow" or "Stamina" or "CastTurning")))
                throw new InvalidOperationException("Invalid approved input list");
            if (actions.EnumerateArray().Select(a => a.GetString()).Distinct().Count() != actions.GetArrayLength())
                throw new InvalidOperationException("Duplicate approved input kind");
            if (!element.GetProperty("ActuationValidated").GetBoolean() && actions.GetArrayLength() != 0)
                throw new InvalidOperationException("Read-only profile cannot approve input");
            var modes = element.TryGetProperty("ValidatedAttackModes", out var modeList) ? modeList : default;
            if (modes.ValueKind != JsonValueKind.Undefined && (modes.ValueKind != JsonValueKind.Array ||
                modes.GetArrayLength() > 3 || modes.EnumerateArray().Any(m => m.ValueKind != JsonValueKind.String ||
                    m.GetString() is not ("Melee" or "Fan" or "HisuyFan")) ||
                modes.EnumerateArray().Select(m => m.GetString()).Distinct().Count() != modes.GetArrayLength()))
                throw new InvalidOperationException("Invalid approved attack modes");
            if (modes.ValueKind == JsonValueKind.Array && modes.GetArrayLength() > 0 &&
                (!element.GetProperty("ActuationValidated").GetBoolean() ||
                 !actions.EnumerateArray().Any(a => a.GetString() == "Attack")))
                throw new InvalidOperationException("Attack mode requires explicit Attack approval");
            // No inherited obfuscated defaults: all names are explicitly present in the signed payload.
            match = (element.Deserialize<GameMemoryProfile>() ?? throw new InvalidOperationException("Invalid signed profile"))
                with { ExternalProfile = true };
        }
        return match;
    }

    public static GameMemoryProfile? Fetch(string gameHash, string commonHash)
    {
        // A packaged, signed baseline works before its first remote publication or during an outage.
        // It cannot add executable logic and is checked with the same embedded public key.
        var local = Path.Combine(AppContext.BaseDirectory, "compatibility", "profiles.json");
        if (File.Exists(local))
        {
            var bytes = File.ReadAllBytes(local);
            var signatureBytes = Convert.FromBase64String(File.ReadAllText(local + ".sig").Trim());
            var bundled = VerifyAndSelect(bytes, signatureBytes, PublicKey, gameHash, commonHash);
            if (bundled is not null) return bundled;
        }
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(5) };
        var payload = GetLimited(client, FeedUrl);
        var signatureText = GetLimited(client, FeedUrl + ".sig");
        return VerifyAndSelect(payload, Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(signatureText).Trim()),
            PublicKey, gameHash, commonHash);
    }

    private static byte[] GetLimited(HttpClient client, string url)
    {
        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > 65536)
            throw new InvalidOperationException("Signed compatibility feed unavailable");
        using var stream = response.Content.ReadAsStream();
        using var limited = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            if (limited.Length + count > 65536) throw new InvalidOperationException("Compatibility feed too large");
            limited.Write(buffer, 0, count);
        }
        return limited.ToArray();
    }

    private static void RequireField(JsonElement element, string name)
    {
        var value = element.GetProperty(name).GetString();
        if (value is null || !FieldName.IsMatch(value)) throw new InvalidOperationException($"Invalid field {name}");
    }

    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
