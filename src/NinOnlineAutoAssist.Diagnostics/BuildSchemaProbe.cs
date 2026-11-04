using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Explicit passive schema calibration; never authorizes input or changes a live fingerprint.</summary>
public static class BuildSchemaProbe
{
    public static int Run(int pid)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline");
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        using var stream = File.OpenRead(Path.Combine(directory, "NinOnline.dll"));
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var result = new List<object>();
        foreach (var h in md.TypeDefinitions)
        {
            var def = md.GetTypeDefinition(h);
            if (def.GetFields().Count < 80 || !def.GetDeclaringType().IsNil) continue;
            var name = md.GetString(def.Namespace) + "." + md.GetString(def.Name);
            var type = module.GetTypeByName(name);
            if (type is null || type.StaticFields.Length < 70) continue;
            result.Add(new { type.Name, Fields = type.StaticFields.Select(f => new { f.Name, Type = f.Type?.Name }).ToArray() });
        }
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
