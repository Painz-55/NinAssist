using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace NinOnlineAutoAssist.Live;

public sealed record GameMemoryProfile
{
	public required string Id { get; init; }

	public required string Sha256 { get; init; }

	public required string CommonSha256 { get; init; }

	public bool ExternalProfile { get; init; }

	public bool ActuationValidated { get; init; } = true;

	public bool ExperimentalTrial { get; init; }

	public string[] ValidatedInputKinds { get; init; } = Array.Empty<string>();

	public bool? PunishComboValidated { get; init; }

	public bool? PunishComboRequiresTarget { get; init; }

	public string[] ValidatedAttackModes { get; init; } = Array.Empty<string>();

	public string Roots { get; init; } = "ElTfx1eaUWfDotOKOf4A.N3S6pdea9c38MU1PQPYi";

	public string Globals { get; init; } = "YZGTHnmxd6HttrIwHLAt.IeAbl1m3zu618JKfosQ9";

	public string Players { get; init; } = "vXFeaSMlkle";

	public string LocalIndex { get; init; } = "eOom5HvkyBm";

	public string? SelectedIndex { get; init; } = "boQmDVeX1PF";

	public SelectedTargetMemoryFields? SelectedTarget { get; init; }

	public string Name { get; init; } = "gwMAu0vTEek";

	public string? Village { get; init; }

	public CastMemoryFields? CastTiming { get; init; }

	public string Map { get; init; } = "MchA5E1VnG0";

	public string PixelX { get; init; } = "jyJA5w2dGAf";

	public string PixelY { get; init; } = "GHtA5hC7y8f";

	public string Facing { get; init; } = "DdmA5VXdD6a";

	public string Vitals { get; init; } = "MXIA5Hxi0Pe";

	public string MaxVitals { get; init; } = "uGiA58yEnh9";

	public string Running { get; init; } = "ILwmbO5H9Wt";

	public string Buffs { get; init; } = "NbdAlOX7FNP";

	public string BuffActive { get; init; } = "bgbAcsBvHyJ";

	public string BuffName { get; init; } = "cPyA1N8efbj";

	public string BuffDuration { get; init; } = "uTFAcdm5RB2";

	public string MapRoot { get; init; } = "SXjeaZQugUZ";

	public string MapIdentity { get; init; } = "JvuAU11GopR";

	public bool MapIdentityInRoots { get; init; }

	public string Tiles { get; init; } = "EuIAUxvdqgZ";

	public string Projectiles { get; init; } = "krteX0oFPhC";

	public static GameMemoryProfile Standalone { get; } = new GameMemoryProfile
	{
		Id = "Standalone 2026-11-03",
		Sha256 = "6081E46E3A0402772DB4F1010A0C3A6781583976232FF014957D032C43CD0AF7",
		CommonSha256 = "B2CCFD06780E0D5DB1024AE11876793C04FF0FC2A13F79158F01E9FF6049022C"
	};

	public static GameMemoryProfile Steam { get; } = new GameMemoryProfile
	{
		Id = "Steam 2026-11-03",
		Sha256 = "0B58A940C17333557C85EA81087195057BFFCE2365E4AC6ECC48C6832FF17E24",
		CommonSha256 = "73D2E22DED4EF731C52E6A6CD9D85228489A58C96C5FAFF7987A751531C14892",
		Roots = "gVH2tTsj2daRAhcLcJ1T.LGydobsjAGN6A2xK6vOy",
		Globals = "X0x91dig41U8QiiCDslq.CmyWN8igTG5jnxOTZKhk",
		Players = "Ho2sFZg9JA0",
		LocalIndex = "FeLiNAOU9tm",
		SelectedIndex = "jI5iQsQxFaI",
		Name = "eeMaZPLGdNI",
		Village = "JL2aZ3OtFAX",
		Map = "Rn6alNPaS0B",
		PixelX = "hmYal0OCtvv",
		PixelY = "lF2alQp5AMQ",
		CastTiming = new CastMemoryFields("TrciZfyQr1Z", "Oc9iZEdH7WR", "fr7i00kKjy2", "T1UaceWEEg4", "ebna00GHAVS"),
		Facing = "wUWalcdPL7Y",
		Vitals = "A5salveOR1O",
		MaxVitals = "OKBalu72IZW",
		Running = "woRiZrGtTix",
		Buffs = "zy4aQnoBvPe",
		BuffActive = "DjtaOiLxCrr",
		BuffName = "aSZaCzBknZh",
		BuffDuration = "bfmaOBwRAkF",
		MapRoot = "x4gsFOhGhn0",
		MapIdentity = "QgCaL1NRqoH",
		Tiles = "i4DaLSwBXIV",
		Projectiles = "ttWsteFlOX1"
	};

	public static GameMemoryProfile Updated20261104 { get; } = new GameMemoryProfile
	{
		Id = "Nin Online 2026-11-04 (Steam / standalone)",
		Sha256 = "3857343C1E5E9146DD5F546D6134D69C057370AF34FA4648484C73E5BB2FF34C",
		CommonSha256 = "DD8F314B470F344E8A903A7B0C91FD46E6163327F3B85A3CA0A230AF0BACA373",
		Roots = "qZXy54Z5TCbXxG5MP2BZ.udrGQWZ5CqCBbCC0jG7F",
		Globals = "w647uNG6KxPuBJCXB1rf.Aoo2CxG66KyAZ75NsE2h",
		Players = "r9MZvLagWW9",
		LocalIndex = "OM7GDFX0dPg",
		SelectedIndex = "knZQ2QNEBE4",
		SelectedTarget = new SelectedTargetMemoryFields("w8qGDsNEwFp", "BXnQ2GyGUKg", "knZQ2QNEBE4", 1),
		Name = "duuQOuUTBJY",
		Village = "o8rQOYIt1ty",
		Map = "wRxQilDTY1L",
		PixelX = "Qe7QiWD2ksK",
		PixelY = "xg2QiA0TJvv",
		Facing = "x7HQiQPZsLI",
		Vitals = "fHgQOwoEbyl",
		MaxVitals = "fyVQOokOv0o",
		Running = "QVgGMJROkU3",
		CastTiming = new CastMemoryFields("Ja3GDo42GCj", "eFZGMAADG0J", "InyGhm238KE", "r4tQinq00hW", "DhXQ0WMjwZR"),
		Buffs = "iw8Q0fchltc",
		BuffActive = "xAiQL0x7sTw",
		BuffName = "kjPQLDIccf4",
		BuffDuration = "FJ5QLissuoH",
		MapRoot = "ameZvGfHXJO",
		MapIdentity = "FoDZvQ5kmNE",
		MapIdentityInRoots = true,
		Tiles = "PrZQrSDFQpW",
		Projectiles = "EXcZCbJ5PI3"
	};

	public bool ApprovesAttackMode(string mode)
	{
		if ((ActuationValidated || ExperimentalTrial) && ValidatedInputKinds.Contains<string>("Attack", StringComparer.Ordinal))
		{
			return ValidatedAttackModes.Contains<string>(mode, StringComparer.Ordinal);
		}
		return false;
	}

	public static GameMemoryProfile Select(string gameHash, string commonHash)
	{
		return new GameMemoryProfile[3] { Standalone, Steam, Updated20261104 }.SingleOrDefault((GameMemoryProfile p) => p.Sha256.Equals(gameHash, StringComparison.OrdinalIgnoreCase) && p.CommonSha256.Equals(commonHash, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"Unsupported game build: par de arquivos não validado (NinOnline {gameHash}, Common {commonHash}). Revalidação necessária; não há fallback por caminho.");
	}

	[CompilerGenerated]
	[SetsRequiredMembers]
	private GameMemoryProfile(GameMemoryProfile original)
	{
		Id = original.Id;
		Sha256 = original.Sha256;
		CommonSha256 = original.CommonSha256;
		ExternalProfile = original.ExternalProfile;
		ActuationValidated = original.ActuationValidated;
		ExperimentalTrial = original.ExperimentalTrial;
		ValidatedInputKinds = original.ValidatedInputKinds;
		PunishComboValidated = original.PunishComboValidated;
		PunishComboRequiresTarget = original.PunishComboRequiresTarget;
		ValidatedAttackModes = original.ValidatedAttackModes;
		Roots = original.Roots;
		Globals = original.Globals;
		Players = original.Players;
		LocalIndex = original.LocalIndex;
		SelectedIndex = original.SelectedIndex;
		SelectedTarget = original.SelectedTarget;
		Name = original.Name;
		Village = original.Village;
		CastTiming = original.CastTiming;
		Map = original.Map;
		PixelX = original.PixelX;
		PixelY = original.PixelY;
		Facing = original.Facing;
		Vitals = original.Vitals;
		MaxVitals = original.MaxVitals;
		Running = original.Running;
		Buffs = original.Buffs;
		BuffActive = original.BuffActive;
		BuffName = original.BuffName;
		BuffDuration = original.BuffDuration;
		MapRoot = original.MapRoot;
		MapIdentity = original.MapIdentity;
		MapIdentityInRoots = original.MapIdentityInRoots;
		Tiles = original.Tiles;
		Projectiles = original.Projectiles;
	}

	public GameMemoryProfile()
	{
	}
}
