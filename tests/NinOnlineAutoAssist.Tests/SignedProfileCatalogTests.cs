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
