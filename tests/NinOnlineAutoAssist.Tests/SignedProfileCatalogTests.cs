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
        Assert.False(updated.ApprovesAttackMode("Fan"));
        Assert.False(updated.ApprovesAttackMode("HisuyFan"));
        Assert.True(updated.PunishComboValidated);
        Assert.True(updated.PunishComboRequiresTarget);
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
