using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only selection candidates for an exact build; never creates or approves a profile.</summary>
public static class TargetSelectionSurvey
{
    public static int Run(int pid, string character, string rootName, string playerArrayName,
        string actorNameField, string expectedHash)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || string.IsNullOrWhiteSpace(character))
            throw new InvalidOperationException("Expected NinOnline and explicit character");
        var path = Path.Combine(Path.GetDirectoryName(process.MainModule!.FileName)!, "NinOnline.dll");
        using (var file = File.OpenRead(path))
            if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game build changed; refusing stale survey");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var domain = runtime.AppDomains.Single();
        var players = module.GetTypeByName(rootName)?.GetStaticFieldByName(playerArrayName)?.ReadObject(domain);
        if (players is not { IsArray: true }) throw new InvalidOperationException("Player array unavailable");
        var matches = Enumerable.Range(1, players.Value.AsArray().Length - 1)
            .Where(i => {
                try { return players.Value.AsArray().GetObjectValue(i).ReadStringField(actorNameField, 64) == character; }
                catch { return false; }
            }).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Character must match exactly one player record");
        var result = new List<object>();
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var definition = metadata.GetTypeDefinition(handle);
            if (definition.GetFields().Count < 70 || !definition.GetDeclaringType().IsNil) continue;
            var type = module.GetTypeByName(metadata.GetString(definition.Namespace) + "." + metadata.GetString(definition.Name));
            if (type is null || type.StaticFields.Length < 70) continue;
            foreach (var field in type.StaticFields)
            {
                try
                {
                    if (!field.IsInitialized(domain) || field.Type is null || field.Type.IsArray ||
                        field.Type.IsPrimitive || field.Type.Name?.StartsWith("System.", StringComparison.Ordinal) == true)
                        continue;
                    var intFields = field.Type.Fields.Where(f => f.Type?.Name == "System.Int32").ToArray();
                    if (intFields.Length is < 2 or > 8) continue;
                    var obj = field.ReadObject(domain);
                    if (obj.IsNull) continue;
                    result.Add(new { Type = type.Name, Field = field.Name, RecordType = field.Type.Name,
                        Values = intFields.Select(f => new { f.Name, Value = f.Read<int>(obj.Address, false) }).ToArray() });
                }
                catch { /* Mutable or unreadable fields never become evidence. */ }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { LocalNameMatches = matches,
            Candidates = result, Warning = "Read-only candidates. Selection requires independent before/after confirmation; no input authorized." }));
        return 0;
    }
}
