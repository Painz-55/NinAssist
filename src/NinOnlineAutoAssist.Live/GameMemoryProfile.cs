namespace NinOnlineAutoAssist.Live;

/// <summary>Build-specific names, selected only by an exact pair of game/Common fingerprints.</summary>
public sealed record GameMemoryProfile
{
    public required string Id { get; init; }
    public required string Sha256 { get; init; }
    public required string CommonSha256 { get; init; }
    public string Roots { get; init; } = LiveMemoryProfile.Roots;
    public string Globals { get; init; } = LiveMemoryProfile.Globals;
    public string Players { get; init; } = LiveMemoryProfile.Players;
    public string LocalIndex { get; init; } = LiveMemoryProfile.LocalIndex;
    public string SelectedIndex { get; init; } = LiveMemoryProfile.SelectedIndex;
    public string Name { get; init; } = LiveMemoryProfile.Name;
    // Null until calibrated for this exact fingerprint; never guess by field order or player name.
    public string? Village { get; init; }
    public CastMemoryFields? CastTiming { get; init; }
    public string Map { get; init; } = LiveMemoryProfile.Map;
    public string PixelX { get; init; } = LiveMemoryProfile.PixelX;
    public string PixelY { get; init; } = LiveMemoryProfile.PixelY;
    public string Facing { get; init; } = LiveMemoryProfile.Facing;
    public string Vitals { get; init; } = LiveMemoryProfile.Vitals;
    public string MaxVitals { get; init; } = LiveMemoryProfile.MaxVitals;
    public string Running { get; init; } = LiveMemoryProfile.Running;
    public string Buffs { get; init; } = "NbdAlOX7FNP";
    public string BuffActive { get; init; } = "bgbAcsBvHyJ";
    public string BuffName { get; init; } = "cPyA1N8efbj";
    public string BuffDuration { get; init; } = "uTFAcdm5RB2";
    public string MapRoot { get; init; } = "SXjeaZQugUZ";
    public string MapIdentity { get; init; } = "JvuAU11GopR";
    public string Tiles { get; init; } = "EuIAUxvdqgZ";
    public string Projectiles { get; init; } = "krteX0oFPhC";

    public static GameMemoryProfile Standalone { get; } = new()
    {
        Id = "Standalone 2026-11-03", Sha256 = LiveMemoryProfile.Sha256, CommonSha256 = LiveMemoryProfile.CommonSha256
    };
    public static GameMemoryProfile Steam { get; } = new()
    {
        Id = "Steam 2026-11-03",
        Sha256 = "0B58A940C17333557C85EA81087195057BFFCE2365E4AC6ECC48C6832FF17E24",
        CommonSha256 = "73D2E22DED4EF731C52E6A6CD9D85228489A58C96C5FAFF7987A751531C14892",
        Roots = "gVH2tTsj2daRAhcLcJ1T.LGydobsjAGN6A2xK6vOy",
        Globals = "X0x91dig41U8QiiCDslq.CmyWN8igTG5jnxOTZKhk",
        Players = "Ho2sFZg9JA0", LocalIndex = "FeLiNAOU9tm", SelectedIndex = "jI5iQsQxFaI",
        Name = "eeMaZPLGdNI", Village = "JL2aZ3OtFAX", Map = "Rn6alNPaS0B", PixelX = "hmYal0OCtvv", PixelY = "lF2alQp5AMQ",
        CastTiming = new("TrciZfyQr1Z", "Oc9iZEdH7WR", "fr7i00kKjy2", "T1UaceWEEg4", "ebna00GHAVS"),
        Facing = "wUWalcdPL7Y", Vitals = "A5salveOR1O", MaxVitals = "OKBalu72IZW", Running = "woRiZrGtTix",
        Buffs = "zy4aQnoBvPe", BuffActive = "DjtaOiLxCrr", BuffName = "aSZaCzBknZh", BuffDuration = "bfmaOBwRAkF",
        MapRoot = "x4gsFOhGhn0", MapIdentity = "QgCaL1NRqoH", Tiles = "i4DaLSwBXIV", Projectiles = "ttWsteFlOX1"
    };
    public static GameMemoryProfile Select(string gameHash, string commonHash)
        => new[] { Standalone, Steam }.SingleOrDefault(p => p.Sha256.Equals(gameHash, StringComparison.OrdinalIgnoreCase) &&
            p.CommonSha256.Equals(commonHash, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unsupported game build: par de arquivos não validado (NinOnline {gameHash}, Common {commonHash}). Revalidação necessária; não há fallback por caminho.");
}

/// <summary>Correlated local UI cast + actor cast + game clock; no spell-specific whitelist.</summary>
public sealed record CastMemoryFields(string GlobalSpell, string GlobalStart, string GameClock, string PlayerSpell, string PlayerEnd);
