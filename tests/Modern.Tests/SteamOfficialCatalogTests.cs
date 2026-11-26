using System.Security.Cryptography;
using System.Text.Json;
using NinOnlineAutoAssist.Live;

namespace Modern.Tests;

public class SteamOfficialCatalogTests
{
    [Fact]
    public void PackagedSteamProfileHasValidSignatureAndRestrictedInputs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "compatibility", "package-profiles.json");
        var profile = SignedProfileCatalog.VerifyAndSelect(File.ReadAllBytes(path),
            Convert.FromBase64String(File.ReadAllText(path + ".sig").Trim()), SignedProfileCatalog.PublicKey,
            "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525",
            "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102");
        Assert.NotNull(profile);
        Assert.Equal("Nin Online Steam 2026-11-26 (validated core; restricted)", profile.Id);
        Assert.Equal(["Turning", "Stamina", "TargetMemory"], profile.ValidatedInputKinds);
        Assert.Empty(profile.ValidatedAttackModes);
    }

    [Fact]
    public void ExactSteamCoreProfileIsAcceptedButUnvalidatedInputsAreRejected()
    {
        var baseline = GameMemoryProfile.Updated20261104;
        var official = baseline with
        {
            Id = "Nin Online Steam 2026-11-26 (validated core; restricted)",
            Sha256 = "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525",
            CommonSha256 = "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102",
            ExperimentalTrial = false, ActuationValidated = true,
            ValidatedInputKinds = ["Turning", "Stamina", "TargetMemory"],
            ValidatedAttackModes = [], PunishComboValidated = false,
            SelectedIndex = null,
            SelectedTarget = new("YPDrG8hKA8g", "iI1YjePn4ca", "Al8Yj3jCFMN", 1)
        };
        GameMemoryProfile? Verify(GameMemoryProfile profile)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, profiles = new[] { profile } });
            var signature = key.SignData(payload, HashAlgorithmName.SHA256);
            return SignedProfileCatalog.VerifyAndSelect(payload, signature, key.ExportSubjectPublicKeyInfoPem(),
                profile.Sha256, profile.CommonSha256);
        }
        Assert.NotNull(Verify(official));
        Assert.Throws<InvalidOperationException>(() => Verify(official with { ValidatedInputKinds = ["Turning", "Stamina", "TargetMemory", "Follow"] }));
        Assert.Throws<InvalidOperationException>(() => Verify(official with { ValidatedAttackModes = ["Fan"] }));
        Assert.Throws<InvalidOperationException>(() => Verify(official with { SelectedTarget = official.SelectedTarget! with { Index = "other" } }));
        Assert.Throws<InvalidOperationException>(() => Verify(official with { CommonSha256 = new string('0', 64) }));
    }
}
