using System.Security.Cryptography;
using System.Text.Json;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class SignedProfileCatalogTests
{
    [Fact]
    public void PublishedManifestSignatureMatchesEmbeddedPublicKey()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var payload = File.ReadAllBytes(Path.Combine(directory, "profiles.json"));
        var signature = Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, "profiles.json.sig")).Trim());
        // Valid signature even when there is no entry for this fingerprint.
        Assert.Null(SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey,
            GameMemoryProfile.Updated20261104.Sha256, GameMemoryProfile.Updated20261104.CommonSha256));
        var current = SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey,
            "6E1C797CC987C8A0481E016CEC6DCE64C5B6118B41BD3D7BEA33997D94F15338",
            "63C3F7FB9380F630DB9ADE5F591E22591810139C0B844BAA413731D1A0A6CAD0");
        Assert.NotNull(current);
        Assert.True(current.ApprovesAttackMode("Melee"));
        Assert.False(current.ApprovesAttackMode("Fan"));
        Assert.False(current.ApprovesAttackMode("HisuyFan"));
        Assert.Contains("Tool", current.ValidatedInputKinds);
        Assert.Contains("Punish", current.ValidatedInputKinds);
        Assert.Null(SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey,
            "B384E2E1EE84A5AC0CB86AAE4F9A4A0A9D41C4F3E68195F9AC434656C9B05A60",
            "CC1E2845C8C7F23E85D2A92A9CE0B5425DC954CA3BE5D514743FB02A0BD1CA9E"));
    }

    [Fact]
    public void BundledNewProfileRequiresUpdatedExecutableAndKeepsUnvalidatedModesBlocked()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var payload = File.ReadAllBytes(Path.Combine(directory, "package-profiles.json"));
        var signature = Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, "package-profiles.json.sig")).Trim());
        var updated = SignedProfileCatalog.VerifyAndSelect(payload, signature, SignedProfileCatalog.PublicKey,
            "B384E2E1EE84A5AC0CB86AAE4F9A4A0A9D41C4F3E68195F9AC434656C9B05A60",
            "CC1E2845C8C7F23E85D2A92A9CE0B5425DC954CA3BE5D514743FB02A0BD1CA9E");
        Assert.NotNull(updated);
        Assert.True(updated.ActuationValidated);
        Assert.Equal(7, updated.ValidatedInputKinds.Length);
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboValidated);
        Assert.True(updated.PunishComboRequiresTarget);
    }

    [Fact]
    public void LatestIntegratedBuildIsBundledOnlyAndHisuyRemainsBlocked()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "34B7902B3081F565FEE693407D065FB65EC8AECBD8D8537E4B4B7638E6353188";
        var common = "B3811B9C3EBE069241CB1710A78DE424254AB8D8BCEE6DB6F22493836E4DC932";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles")); // Older installed clients lack new combo gates.
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Equal(7, updated.ValidatedInputKinds.Length);
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboValidated);
        Assert.True(updated.PunishComboRequiresTarget);
    }

    [Fact]
    public void StormFinBuildIsBundledOnlyAndFanTurnHasExplicitApproval()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "F16B1C45EADC1AB64C86128A1B0D45E2257A4E6C72E87106D2C80EE0A11154F4";
        var common = "23E3A6B8DF264F530AA27B072179366A4C710755219A62FCD64481EEA1DBDDE7";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Contains("Turning", updated.ValidatedInputKinds);
        Assert.Contains("Attack", updated.ValidatedInputKinds);
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboRequiresTarget);
    }

    [Fact]
    public void NovemberEighthBuildIsOnlyBundledAndKeepsHisuyBlocked()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "E41A4F4CBB46601A889AB6D89E0D8032F4D375F91384186E0165603493BC6A1A";
        var common = "B97DA9A07DE4376C3E87905D518D43EE641697D0A7E07CFAD1EA5166FA02A898";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Contains("Turning", updated.ValidatedInputKinds);
        Assert.Contains("Attack", updated.ValidatedInputKinds);
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboRequiresTarget);
    }

    [Fact]
    public void NovemberNinthBuildIsBundledOnlyWithFanTurnApproved()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "C7E58B1281D3435B82DC8A5F722D794E59F01EED74368EB837F7D946B4C9EA8B";
        var common = "E457EA854E7F9950FD82066D4DC1BA9104BBFD08FBE2160A351EA9DA4773F08B";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Contains("Turning", updated.ValidatedInputKinds);
        Assert.Contains("Attack", updated.ValidatedInputKinds);
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboRequiresTarget);
    }

    [Fact]
    public void NovemberTenthBuildIsBundledOnlyWithTargetGateAndHisuyBlocked()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "91DFB75647B4650A538CA18837B14EF05C10A32A23953BECE85E3F09198C8652";
        var common = "92D7FE521A3A362B91FB12F35F8A8B6549747283F10462047F1CF80E8F69C548";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Equal(7, updated.ValidatedInputKinds.Length);
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboValidated);
        Assert.True(updated.PunishComboRequiresTarget);
        Assert.Equal(1, updated.SelectedTarget?.PlayerKind);
    }

    [Fact]
    public void NovemberEleventhBuildUsesReidentifiedBuffArrayAndStaysBundledOnly()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "C269AB30F2C4CBAF335D885944045D6C45E6833A0DDD76CFD76E4ECC819F7A01";
        var common = "074D50E384F8E457F777FC0B4A014D035543EF07D1D1787690FAE7F31438ECB0";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Equal("GLbAtOZVxwG", updated.Buffs);
        Assert.Equal(7, updated.ValidatedInputKinds.Length);
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboValidated);
        Assert.True(updated.PunishComboRequiresTarget);
        Assert.Equal(1, updated.SelectedTarget?.PlayerKind);
    }

    [Fact]
    public void NovemberThirteenthBuildUsesTypedTargetWithoutLegacyIndexAndStaysBundledOnly()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        var game = "5EBCBDBB46FA2A1E124C2DF55277C2E61D1C3D02C2B0C4A87B0C2D66124B20CB";
        var common = "BC5C93AF9A871523C197DE7D3BEBCF767C179DC7C187C78E8E8A87C31335CABE";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Equal(7, updated.ValidatedInputKinds.Length);
        Assert.Null(updated.SelectedIndex);
        Assert.Equal("KqGq8aM0LF0", updated.Village);
        Assert.Equal(1, updated.SelectedTarget?.PlayerKind);
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboRequiresTarget);
    }

    [Fact]
    public void NovemberFourteenthBuildIsBundledOnlyWithExplicitTrialledModesAndTargetGate()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "compatibility");
        const string game = "EB6F1AA22471A204401FA536CD75455555B9A026C705C6D799498A0D76E22109";
        const string common = "08E16512C3A95A6C18D5381BE068E9908FE3505524991BD7CD8B2AA68F1CA67E";
        GameMemoryProfile? Select(string name) => SignedProfileCatalog.VerifyAndSelect(
            File.ReadAllBytes(Path.Combine(directory, name + ".json")),
            Convert.FromBase64String(File.ReadAllText(Path.Combine(directory, name + ".json.sig")).Trim()),
            SignedProfileCatalog.PublicKey, game, common);
        Assert.Null(Select("profiles"));
        var updated = Assert.IsType<GameMemoryProfile>(Select("package-profiles"));
        Assert.True(updated.ActuationValidated);
        Assert.Equal(7, updated.ValidatedInputKinds.Length);
        Assert.True(updated.ApprovesAttackMode("Melee"));
        Assert.True(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboValidated);
        Assert.True(updated.PunishComboRequiresTarget);
        Assert.Null(updated.SelectedIndex);
        Assert.Equal(1, updated.SelectedTarget?.PlayerKind);
    }

    private static (byte[] Payload, byte[] Signature, string PublicKey) Sign(object document)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        return (bytes, key.SignData(bytes, HashAlgorithmName.SHA256), key.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public void ValidSignedProfileRequiresExactPairAndExplicitFields()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with { ActuationValidated = false } } });
        var found = SignedProfileCatalog.VerifyAndSelect(signed.Payload, signed.Signature, signed.PublicKey,
            p.Sha256, p.CommonSha256);
        Assert.NotNull(found);
        Assert.True(found.ExternalProfile);
        Assert.False(found.ActuationValidated);
        Assert.Null(SignedProfileCatalog.VerifyAndSelect(signed.Payload, signed.Signature, signed.PublicKey,
            p.Sha256, GameMemoryProfile.Steam.CommonSha256));
    }

    [Fact]
    public void TypedTargetCanOmitUnusedLegacyIndexAndUnknownVillageWithoutInheritingDefaults()
    {
        var p = GameMemoryProfile.Updated20261104 with { SelectedIndex = null, Village = null,
            ActuationValidated = false, ValidatedInputKinds = [], ValidatedAttackModes = [] };
        var signed = Sign(new { schema = 1, profiles = new[] { p } });
        var found = SignedProfileCatalog.VerifyAndSelect(signed.Payload, signed.Signature, signed.PublicKey,
            p.Sha256, p.CommonSha256)!;
        Assert.Null(found.SelectedIndex);
        Assert.Null(found.Village);
        Assert.NotNull(found.SelectedTarget);
        Assert.False(found.ActuationValidated);
    }

    [Fact]
    public void LegacyTargetWithoutSelectedIndexIsRejected()
    {
        var p = GameMemoryProfile.Updated20261104 with { SelectedTarget = null, SelectedIndex = null };
        var signed = Sign(new { schema = 1, profiles = new[] { p } });
        Assert.ThrowsAny<Exception>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Fact]
    public void UnsignedOrModifiedManifestCannotEnableInputs()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p } });
        signed.Payload[^3] ^= 1;
        Assert.Throws<CryptographicException>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Fact]
    public void MissingFieldsDoNotInheritOldObfuscatedNames()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { new { p.Sha256, p.CommonSha256, p.Id,
            p.ActuationValidated, p.MapIdentityInRoots } } });
        Assert.ThrowsAny<Exception>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Fact]
    public void DuplicatePairIsRejected()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p, p } });
        Assert.Throws<InvalidOperationException>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Fact]
    public void ApprovedInputsAreExplicitPerFeature()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = true, ValidatedInputKinds = ["Stamina"]
        } } });
        var found = SignedProfileCatalog.VerifyAndSelect(signed.Payload, signed.Signature, signed.PublicKey,
            p.Sha256, p.CommonSha256)!;
        Assert.Equal(["Stamina"], found.ValidatedInputKinds);
        Assert.DoesNotContain("Attack", found.ValidatedInputKinds);
    }

    [Fact]
    public void SignedSubTurnTrialCanExplicitlyBlockComboWithoutChangingOlderProfiles()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = true, ValidatedInputKinds = ["Turning", "Punish"],
            PunishComboValidated = false
        } } });
        var found = SignedProfileCatalog.VerifyAndSelect(signed.Payload, signed.Signature, signed.PublicKey,
            p.Sha256, p.CommonSha256)!;
        Assert.False(found.PunishComboValidated);
        Assert.Null(p.PunishComboValidated);
        var targeted = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = true, ValidatedInputKinds = ["Turning", "Punish"],
            PunishComboValidated = true, PunishComboRequiresTarget = true
        } } });
        var combo = SignedProfileCatalog.VerifyAndSelect(targeted.Payload, targeted.Signature, targeted.PublicKey,
            p.Sha256, p.CommonSha256)!;
        Assert.True(combo.PunishComboValidated);
        Assert.True(combo.PunishComboRequiresTarget);
        Assert.Null(p.PunishComboRequiresTarget);
    }

    [Fact]
    public void SignedAttackApprovalDoesNotAuthorizeOtherModes()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = true, ValidatedInputKinds = ["Attack"], ValidatedAttackModes = ["Melee"]
        } } });
        var found = SignedProfileCatalog.VerifyAndSelect(signed.Payload, signed.Signature, signed.PublicKey,
            p.Sha256, p.CommonSha256)!;
        Assert.True(found.ApprovesAttackMode("Melee"));
        Assert.False(found.ApprovesAttackMode("Fan"));
        Assert.False(found.ApprovesAttackMode("HisuyFan"));
        Assert.False((found with { ValidatedAttackModes = [] }).ApprovesAttackMode("Melee"));
        Assert.False((found with { ValidatedInputKinds = [] }).ApprovesAttackMode("Melee"));
        signed.Payload[^3] ^= 1;
        Assert.Throws<CryptographicException>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Theory]
    [InlineData("Unvalidated")]
    [InlineData("Fan,Fan")]
    public void MalformedOrDuplicateAttackModesFailClosed(string modes)
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = true, ValidatedInputKinds = ["Attack"], ValidatedAttackModes = modes.Split(',')
        } } });
        Assert.Throws<InvalidOperationException>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Fact]
    public void AttackModesWithoutAttackInputFailClosed()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = true, ValidatedInputKinds = ["Tool"], ValidatedAttackModes = ["Melee"]
        } } });
        Assert.Throws<InvalidOperationException>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }

    [Fact]
    public void ReadOnlyProfileCannotApproveAnyAction()
    {
        var p = GameMemoryProfile.Updated20261104;
        var signed = Sign(new { schema = 1, profiles = new[] { p with
        {
            ActuationValidated = false, ValidatedInputKinds = ["Attack"]
        } } });
        Assert.Throws<InvalidOperationException>(() => SignedProfileCatalog.VerifyAndSelect(
            signed.Payload, signed.Signature, signed.PublicKey, p.Sha256, p.CommonSha256));
    }
}
