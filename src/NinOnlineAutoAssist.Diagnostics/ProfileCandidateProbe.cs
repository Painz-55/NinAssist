using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only candidate discovery. It does NOT promote a candidate to a trusted profile.</summary>
public static class ProfileCandidateProbe
{
    public static int Run(int pid)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        var game = Path.Combine(directory, "NinOnline.dll");
        var common = Path.Combine(directory, "Common.dll");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        using var file = File.OpenRead(game);
        using var pe = new PEReader(file);
        var md = pe.GetMetadataReader();
        var candidates = new List<object>();
        var domain = runtime.AppDomains.Single();
        foreach (var handle in md.TypeDefinitions)
        {
            var definition = md.GetTypeDefinition(handle);
            if (!definition.GetDeclaringType().IsNil || definition.GetFields().Count < 20) continue;
            var name = md.GetString(definition.Namespace) + "." + md.GetString(definition.Name);
            var type = module.GetTypeByName(name);
            if (type is null) continue;
            foreach (var field in type.StaticFields)
            {
                try
                {
                    if (field.Type?.IsArray != true || !field.IsInitialized(domain)) continue;
                    var arrayObject = field.ReadObject(domain);
                    if (!arrayObject.IsArray) continue;
                    var array = arrayObject.AsArray();
                    if (array.Length is < 100 or > 2000) continue;
                    var element = field.Type.ComponentType;
                    if (element is null || element.Name?.StartsWith("Common.", StringComparison.Ordinal) == true ||
                        !element.Fields.Any(f => f.Type?.Name == "System.String") ||
                        element.Fields.Count(f => f.Type?.Name == "System.Int32[]") < 2) continue;
                    candidates.Add(new { Root = type.Name, Array = field.Name, Length = array.Length,
                        ActorType = element.Name,
                        CandidateNames = element.Fields.Where(f => f.Type?.Name == "System.String").Select(f => f.Name),
                        CandidateVitals = element.Fields.Where(f => f.Type?.Name == "System.Int32[]").Select(f => f.Name) });
                }
                catch { /* A moving/uninitialized candidate is not evidence. */ }
            }
        }
        using var commonFile = File.OpenRead(common);
        file.Position = 0; // PEReader moved the stream; fingerprint must cover the entire assembly.
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            GameSha256 = Convert.ToHexString(SHA256.HashData(file)),
            CommonSha256 = Convert.ToHexString(SHA256.HashData(commonFile)),
            PlayerArrayCandidates = candidates,
            Warning = "CANDIDATES ONLY: no local identity, target, coordinate or buff semantics proven; no input enabled."
        }));
        return 0;
    }
}
