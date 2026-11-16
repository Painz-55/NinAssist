using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>One passive snapshot of exact-profile global vector candidates; no field is assumed to be the camera.</summary>
public static class CameraCandidateProbe
{
    public static int Run(int pid, string character, string manifest)
    {
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
        var directory = Path.GetDirectoryName(process.MainModule?.FileName)!;
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        var profile = SignedProfileCatalog.VerifyAndSelect(File.ReadAllBytes(manifest),
            Convert.FromBase64String(File.ReadAllText(manifest + ".sig").Trim()), SignedProfileCatalog.PublicKey,
            Hash(Path.Combine(directory, "NinOnline.dll")), Hash(Path.Combine(directory, "Common.dll")))
            ?? throw new InvalidOperationException("No signed exact-pair profile");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        runtime.FlushCachedData();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var globals = module.GetTypeByName(profile.Globals)!;
        var roots = module.GetTypeByName(profile.Roots)!;
        var domain = runtime.AppDomains.Single();
        var localIndex = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
        var local = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray().GetObjectValue(localIndex);
        if (local.IsNull || local.ReadStringField(profile.Name, 64) != character)
            throw new InvalidOperationException("Local identity changed");
        var vectors = globals.StaticFields.Where(f => f.Type?.Name is "NinEngine.Core.Vector2f" or "System.Numerics.Vector2")
            .Select(field =>
            {
                try
                {
                    if (!field.IsInitialized(domain)) return null;
                    var value = field.ReadStruct(domain);
                    return new { field.Name, Type = field.Type?.Name,
                        Components = value.Type?.Fields.Where(f => f.Type?.Name is "System.Single" or "System.Double" or "System.Int32")
                            .Select(f => new { f.Name, Value = f.Type?.Name switch
                            {
                                "System.Single" => (double)value.ReadField<float>(f.Name!),
                                "System.Double" => value.ReadField<double>(f.Name!),
                                _ => value.ReadField<int>(f.Name!)
                            } }).ToArray() };
                }
                catch { return null; }
            }).Where(x => x is not null).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { Local = character, LocalIndex = localIndex,
            PixelX = local.ReadField<float>(profile.PixelX), PixelY = local.ReadField<float>(profile.PixelY), Vectors = vectors,
            Warning = "Read-only vector candidates; no camera semantics or projection approved." }));
        return 0;
    }
}
