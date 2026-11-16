using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive exact-build render-view candidates. Does not select a camera or send input.</summary>
public static class ViewCandidateProbe
{
    public static int Run(int pid, string character, string manifest)
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
        var directory = Path.GetDirectoryName(process.MainModule?.FileName)!;
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        var gameHash = Hash(Path.Combine(directory, "NinOnline.dll"));
        var commonHash = Hash(Path.Combine(directory, "Common.dll"));
        var candidate = !File.Exists(manifest + ".sig");
        var profile = candidate
            ? JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(manifest))
            : SignedProfileCatalog.VerifyAndSelect(File.ReadAllBytes(manifest),
                Convert.FromBase64String(File.ReadAllText(manifest + ".sig").Trim()), SignedProfileCatalog.PublicKey,
                gameHash, commonHash);
        if (profile is null || profile.Sha256 != gameHash || profile.CommonSha256 != commonHash ||
            (candidate && (profile.ActuationValidated || !profile.ExternalProfile ||
                profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0)))
            throw new InvalidOperationException("Exact-pair read-only profile required");
        var oldBuild = gameHash == "3B1AAF914AE0EA2268CEE6C6E95960C2A04CDAC383E0C540D98752B12EF96842";
        if (!oldBuild && gameHash != "C3F7DF26A0C00D72291DEFC67F219BD54909AE9062160329DE4AD6956110CB72")
            throw new InvalidOperationException("View owner is build-specific; no fallback");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        runtime.FlushCachedData();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var domain = runtime.AppDomains.Single();
        var globals = module.GetTypeByName(profile.Globals)!;
        var roots = module.GetTypeByName(profile.Roots)!;
        var index = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
        var local = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray().GetObjectValue(index);
        if (local.IsNull || local.ReadStringField(profile.Name, 64) != character)
            throw new InvalidOperationException("Local identity changed");
        var owner = module.GetTypeByName(oldBuild ? "wMWLcvW2ArKRj9nhY3bg.TyDWRPW2l05BDoekEyUt"
            : "bfh79MUAUg04A0AkOIDe.XueWakUAoTIroqRVa3KM")!;
        var views = (oldBuild ? new[] { "wdPWwfhkUBE", "xgcWweIHSdJ" }
            : new[] { "uFEUSWgHkBt", "AwCUSQuJJUb" }).Select(name =>
        {
            try
            {
                var obj = owner.GetStaticFieldByName(name)!.ReadObject(domain);
                if (obj.IsNull || obj.Type?.Name != "NinEngine.Graphics.View") return null;
                var center = obj.ReadValueTypeField("myCenter");
                var size = obj.ReadValueTypeField("mySize");
                var viewport = obj.ReadValueTypeField("myViewport");
                return new { Name = name, CenterX = center.ReadField<float>("X"), CenterY = center.ReadField<float>("Y"),
                    SizeX = size.ReadField<float>("X"), SizeY = size.ReadField<float>("Y"),
                    Viewport = viewport.Type?.Fields.Select(f => new { f.Name, Value = f.Read<float>(viewport.Address, true) }).ToArray() };
            }
            catch { return null; }
        }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { LocalPixelX = local.ReadField<float>(profile.PixelX),
            LocalPixelY = local.ReadField<float>(profile.PixelY), Views = views,
            Warning = "Passive view candidates only. Game render-view and scale still require validation." }));
        return 0;
    }
}
