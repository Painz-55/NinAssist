using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class GameMemoryProfileTests
{
    [Fact]
    public void OriginalBuildKeepsItsExactMapping()
    {
        var profile = GameMemoryProfile.Select(LiveMemoryProfile.Sha256, LiveMemoryProfile.CommonSha256);
        Assert.Same(GameMemoryProfile.Standalone, profile);
        Assert.Equal(LiveMemoryProfile.Roots, profile.Roots);
        Assert.Equal(LiveMemoryProfile.Globals, profile.Globals);
        Assert.Equal(LiveMemoryProfile.Players, profile.Players);
        Assert.Equal(LiveMemoryProfile.LocalIndex, profile.LocalIndex);
        Assert.Equal(LiveMemoryProfile.SelectedIndex, profile.SelectedIndex);
        Assert.Equal(LiveMemoryProfile.Name, profile.Name);
        Assert.Equal(LiveMemoryProfile.Map, profile.Map);
        Assert.Equal(LiveMemoryProfile.PixelX, profile.PixelX);
        Assert.Equal(LiveMemoryProfile.PixelY, profile.PixelY);
        Assert.Equal(LiveMemoryProfile.Facing, profile.Facing);
        Assert.Equal(LiveMemoryProfile.Vitals, profile.Vitals);
        Assert.Equal(LiveMemoryProfile.MaxVitals, profile.MaxVitals);
        Assert.Equal(LiveMemoryProfile.Running, profile.Running);
        Assert.Equal("NbdAlOX7FNP", profile.Buffs);
        Assert.Equal("SXjeaZQugUZ", profile.MapRoot);
        Assert.Equal("EuIAUxvdqgZ", profile.Tiles);
        Assert.Equal("krteX0oFPhC", profile.Projectiles);
    }

    [Fact]
    public void SteamBuildSelectsOnlyItsOwnNamesIncludingBuffsAndTerrain()
    {
        var steam = GameMemoryProfile.Steam;
        Assert.Same(steam, GameMemoryProfile.Select(steam.Sha256, steam.CommonSha256));
        Assert.Equal("Ho2sFZg9JA0", steam.Players);
        Assert.Equal("FeLiNAOU9tm", steam.LocalIndex);
        Assert.Equal("jI5iQsQxFaI", steam.SelectedIndex);
        Assert.Equal("JL2aZ3OtFAX", steam.Village);
        Assert.Equal("zy4aQnoBvPe", steam.Buffs);
        Assert.Equal("aSZaCzBknZh", steam.BuffName);
        Assert.Equal("bfmaOBwRAkF", steam.BuffDuration);
        Assert.Equal("DjtaOiLxCrr", steam.BuffActive);
        Assert.Equal("woRiZrGtTix", steam.Running);
        Assert.Equal("x4gsFOhGhn0", steam.MapRoot);
        Assert.Equal("QgCaL1NRqoH", steam.MapIdentity);
        Assert.Equal("i4DaLSwBXIV", steam.Tiles);
        Assert.Equal("ttWsteFlOX1", steam.Projectiles);
        // No inherited standalone obfuscated field is accidentally left in the Steam descriptor.
        foreach (var property in typeof(GameMemoryProfile).GetProperties().Where(p =>
            p.PropertyType == typeof(string) && !p.GetMethod!.IsStatic))
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(steam)));
            Assert.NotEqual(property.GetValue(GameMemoryProfile.Standalone), property.GetValue(steam));
        }
    }

    [Fact]
    public void MixedOrUnknownFingerprintPairsNeverFallbackToEitherProfile()
    {
        var old = GameMemoryProfile.Standalone; var steam = GameMemoryProfile.Steam;
        foreach (var (game, common) in new[] { (old.Sha256, steam.CommonSha256), (steam.Sha256, old.CommonSha256),
            ("unknown", old.CommonSha256), (steam.Sha256, "unknown"), ("", "") })
            Assert.Throws<InvalidOperationException>(() => GameMemoryProfile.Select(game, common));
    }

    [Fact]
    public void HexCasingDoesNotChangeTheSelectedBuild()
    {
        var steam = GameMemoryProfile.Steam;
        Assert.Same(steam, GameMemoryProfile.Select(steam.Sha256.ToLowerInvariant(), steam.CommonSha256.ToLowerInvariant()));
    }

    [Fact]
    public void UpdatedBuildUsesItsOwnCompleteValidatedDescriptor()
    {
        var profile = GameMemoryProfile.Updated20261104;
        Assert.Same(profile, GameMemoryProfile.Select(profile.Sha256.ToLowerInvariant(), profile.CommonSha256.ToLowerInvariant()));
        Assert.Equal("OM7GDFX0dPg", profile.LocalIndex);
        Assert.Equal("duuQOuUTBJY", profile.Name);
        Assert.Equal("o8rQOYIt1ty", profile.Village);
        Assert.Equal("fHgQOwoEbyl", profile.Vitals);
        Assert.Equal("fyVQOokOv0o", profile.MaxVitals);
        Assert.Equal("QVgGMJROkU3", profile.Running);
        Assert.Equal(new SelectedTargetMemoryFields("w8qGDsNEwFp", "BXnQ2GyGUKg", "knZQ2QNEBE4", 1), profile.SelectedTarget);
        Assert.Equal(new CastMemoryFields("Ja3GDo42GCj", "eFZGMAADG0J", "InyGhm238KE", "r4tQinq00hW", "DhXQ0WMjwZR"), profile.CastTiming);
        Assert.Equal("iw8Q0fchltc", profile.Buffs);
        Assert.Equal("xAiQL0x7sTw", profile.BuffActive);
        Assert.Equal("kjPQLDIccf4", profile.BuffName);
        Assert.Equal("FJ5QLissuoH", profile.BuffDuration);
        Assert.True(profile.MapIdentityInRoots);
        Assert.Equal("FoDZvQ5kmNE", profile.MapIdentity);
        Assert.Equal("PrZQrSDFQpW", profile.Tiles);
        Assert.Equal("EXcZCbJ5PI3", profile.Projectiles);
        foreach (var property in typeof(GameMemoryProfile).GetProperties().Where(p => p.PropertyType == typeof(string) && !p.GetMethod!.IsStatic))
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)property.GetValue(profile)));
            Assert.NotEqual(property.GetValue(GameMemoryProfile.Steam), property.GetValue(profile));
            Assert.NotEqual(property.GetValue(GameMemoryProfile.Standalone), property.GetValue(profile));
        }
    }

    [Fact]
    public void UpdatedGameNeverAcceptsMixedPreviousCommonAssembly()
    {
        var profiles = new[] { GameMemoryProfile.Standalone, GameMemoryProfile.Steam, GameMemoryProfile.Updated20261104 };
        foreach (var game in profiles)
            foreach (var common in profiles.Where(p => p != game))
                Assert.Throws<InvalidOperationException>(() => GameMemoryProfile.Select(game.Sha256, common.CommonSha256));
    }
}
