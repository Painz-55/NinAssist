using System.Security.Cryptography;
using System.Text.Json;
using NinOnlineAutoAssist.Live;
using static NinOnlineAutoAssist.Diagnostics.FieldMetadata;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Offline candidate crosswalk only. Never writes a profile or signature and never approves input.</summary>
public static class ProfileSchemaDiff
{
    public sealed record NearbyMatch(string Field, int Index, string Type);
    public sealed record BaselineMetadata(string GameSha256, string CommonSha256, string ProfileId, TypeEntry[] Types);

    public static int Capture(string currentDll, string profilesPath)
    {
        var (gameHash, commonHash) = Fingerprint(currentDll);
        var payload = File.ReadAllBytes(profilesPath);
        var signature = Convert.FromBase64String(File.ReadAllText(profilesPath + ".sig").Trim());
        var profile = SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey,
            gameHash, commonHash) ?? throw new InvalidOperationException("No signed profile for this exact DLL pair");
        Console.WriteLine(JsonSerializer.Serialize(new BaselineMetadata(gameHash, commonHash, profile.Id,
            FieldMetadata.ReadSchema(currentDll))));
        return 0;
    }

    public static int Run(string baselinePath, string currentDll, string profilesPath, string baselineId)
    {
        using var baselineDocument = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var packaged = baselineDocument.RootElement.ValueKind == JsonValueKind.Object;
        var captured = packaged ? baselineDocument.RootElement.Deserialize<BaselineMetadata>() : null;
        var old = packaged ? captured?.Types : baselineDocument.RootElement.Deserialize<TypeEntry[]>();
        if (old is null) throw new InvalidOperationException("Missing baseline metadata");
        var current = FieldMetadata.ReadSchema(currentDll);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(profilesPath));
        var entries = manifest.RootElement.GetProperty("profiles").EnumerateArray()
            .Where(p => p.GetProperty("Id").GetString() == baselineId).ToArray();
        if (entries.Length != 1) throw new InvalidOperationException("Select exactly one baseline profile Id");
        var profile = entries[0].Deserialize<GameMemoryProfile>()
            ?? throw new InvalidOperationException("Invalid baseline profile");
        if (packaged && (captured!.ProfileId != profile.Id || captured.GameSha256 != profile.Sha256 ||
            captured.CommonSha256 != profile.CommonSha256))
            throw new InvalidOperationException("Baseline metadata does not match selected signed profile fingerprints");
        if (packaged)
        {
            var payload = File.ReadAllBytes(profilesPath);
            var signature = Convert.FromBase64String(File.ReadAllText(profilesPath + ".sig").Trim());
            if (SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey,
                    captured!.GameSha256, captured.CommonSha256)?.Id != profile.Id)
                throw new InvalidOperationException("Baseline manifest signature or profile mismatch");
        }
        var oldRoot = old.Single(t => t.Name == profile.Roots);
        var oldGlobals = old.Single(t => t.Name == profile.Globals);
        var oldActor = old.Single(t => t.Fields.Any(f => f.Name == profile.Name));
        var roles = new (string Group, string Role, string? Name)[]
        {
            ("Roots", "Players", profile.Players), ("Roots", "MapRoot", profile.MapRoot),
            ("Roots", "MapIdentity", profile.MapIdentity), ("Roots", "Projectiles", profile.Projectiles),
            ("Globals", "LocalIndex", profile.LocalIndex), ("Globals", "Running", profile.Running),
            ("Globals", "SelectedTarget.GlobalObject", profile.SelectedTarget?.GlobalObject),
            ("Globals", "Cast.GlobalSpell", profile.CastTiming?.GlobalSpell),
            ("Globals", "Cast.GlobalStart", profile.CastTiming?.GlobalStart),
            ("Globals", "Cast.GameClock", profile.CastTiming?.GameClock),
            ("Actor", "Name", profile.Name), ("Actor", "Village", profile.Village),
            ("Actor", "Map", profile.Map), ("Actor", "PixelX", profile.PixelX),
            ("Actor", "PixelY", profile.PixelY), ("Actor", "Facing", profile.Facing),
            ("Actor", "Vitals", profile.Vitals), ("Actor", "MaxVitals", profile.MaxVitals),
            ("Actor", "Buffs", profile.Buffs),
            ("Actor", "Cast.PlayerSpell", profile.CastTiming?.PlayerSpell),
            ("Actor", "Cast.PlayerEnd", profile.CastTiming?.PlayerEnd)
        };
        var groups = new[] { (Label: "Roots", Schema: oldRoot), (Label: "Globals", Schema: oldGlobals),
            (Label: "Actor", Schema: oldActor) };
        var ranked = groups.Select(g => new { g.Label, Baseline = g.Schema.Name,
            Top = current.Select(t => new { Type = t, Score = Similarity(g.Schema, t) })
                .OrderByDescending(x => x.Score).Take(2).ToArray() }).ToArray();
        var mappings = roles.Select(r =>
        {
            var group = groups.Single(g => g.Label == r.Group).Schema;
            var selected = ranked.Single(g => g.Label == r.Group);
            var ordinal = Array.FindIndex(group.Fields, f => f.Name == r.Name);
            var ambiguous = selected.Top.Length < 2 || selected.Top[0].Score <= selected.Top[1].Score;
            var found = ordinal >= 0 && !ambiguous && ordinal < selected.Top[0].Type.Fields.Length;
            var baselineType = ordinal >= 0 ? group.Fields[ordinal].Type : null;
            var candidate = found ? selected.Top[0].Type.Fields[ordinal] : null;
            var nearby = ordinal < 0 || ambiguous || baselineType is null ? Array.Empty<NearbyMatch>() : selected.Top[0].Type.Fields
                .Select((f, i) => new NearbyMatch(f.Name, i, f.Type))
                .Where(f => Math.Abs(f.Index - ordinal) is > 0 and <= 6 && f.Type == baselineType)
                .ToArray();
            return new { r.Group, r.Role, BaselineField = r.Name, Ordinal = ordinal,
                CandidateField = candidate?.Name, BaselineType = baselineType, CandidateType = candidate?.Type,
                SameType = candidate is not null && candidate.Type == baselineType,
                NearbySameType = nearby,
                NeedsIndependentValidation = true };
        }).ToArray();
        var (gameHash, commonHash) = Fingerprint(currentDll);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            GameSha256 = gameHash, CommonSha256 = commonHash,
            Baseline = profile.Id,
            BaselineFingerprintVerified = packaged,
            Types = ranked.Select(g => new { Group = g.Label, g.Baseline,
                Candidate = g.Top[0].Type.Name, Score = g.Top[0].Score,
                Ambiguous = g.Top.Length < 2 || g.Top[0].Score <= g.Top[1].Score }),
            Mappings = mappings,
            NotMapped = new[] { "SelectedTarget.Kind/Index", "BuffActive/Name/Duration", "Tiles/Map object" },
            Warning = "CANDIDATES ONLY. Ordinal/type similarity is not semantic validation. No profile, signature, or input approval produced."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static (string Game, string Common) Fingerprint(string dll)
    {
        using var game = File.OpenRead(dll);
        using var common = File.OpenRead(Path.Combine(Path.GetDirectoryName(dll)!, "Common.dll"));
        return (Convert.ToHexString(SHA256.HashData(game)), Convert.ToHexString(SHA256.HashData(common)));
    }

    // Obfuscated type names change every build. Only stable primitive/framework type signatures count.
    public static int Similarity(TypeEntry baseline, TypeEntry candidate)
    {
        var score = -Math.Abs(baseline.Fields.Length - candidate.Fields.Length) * 3;
        for (var i = 0; i < Math.Min(baseline.Fields.Length, candidate.Fields.Length); i++)
        {
            var type = baseline.Fields[i].Type;
            if ((type.StartsWith("System.", StringComparison.Ordinal) ||
                 type.StartsWith("Common.", StringComparison.Ordinal) ||
                 type.StartsWith("client.", StringComparison.Ordinal)) && type == candidate.Fields[i].Type)
                score++;
        }
        return score;
    }
}
