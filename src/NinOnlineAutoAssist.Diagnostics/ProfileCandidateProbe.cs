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
    // Read-only evidence for an explicitly supplied character. A matching name is NOT proof of local identity.
    public static int Identify(int pid, string character)
    {
        if (string.IsNullOrWhiteSpace(character) || character.Length > 64) return 2;
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var domain = runtime.AppDomains.Single();
        var matches = new List<object>();
        var matchIndices = new HashSet<int>();
        var arrayCounts = new List<object>();
        var indexCandidates = new List<object>();
        foreach (var root in new[] { "oRGErqTKUmEPMiRFsMdt.ILqvIRTizh9luQa4ZLwE" })
        {
            var type = module.GetTypeByName(root);
            foreach (var field in type?.StaticFields ?? [])
            {
                if (field.Type?.IsArray != true || !field.IsInitialized(domain)) continue;
                try
                {
                    var array = field.ReadObject(domain);
                    if (!array.IsArray || array.AsArray().Length is < 100 or > 2000) continue;
                    var element = field.Type.ComponentType;
                    if (element is null || element.Fields.Count(f => f.Type?.Name == "System.Int32[]") < 2) continue;
                    var entries = array.AsArray();
                    var occupied = 0;
                    var named = 0;
                    for (var i = 0; i < entries.Length; i++)
                    {
                        var obj = entries.GetObjectValue(i);
                        if (obj.IsNull) continue;
                        occupied++;
                        foreach (var nameField in obj.Type!.Fields.Where(f => f.Type?.Name == "System.String"))
                        {
                            var text = obj.ReadStringField(nameField.Name!, 64);
                            if (!string.IsNullOrWhiteSpace(text)) named++;
                            if (!string.Equals(text, character, StringComparison.OrdinalIgnoreCase)) continue;
                            matchIndices.Add(i);
                            matches.Add(new { Root = root, Array = field.Name, Index = i, ActorType = element.Name,
                                NameField = nameField.Name,
                                ActorNumbers = element.Fields.Select(f =>
                                {
                                    try
                                    {
                                        object? value = f.Type?.Name switch
                                        {
                                            "System.Int32" => f.Read<int>(obj.Address, false),
                                            "System.Int64" => f.Read<long>(obj.Address, false),
                                            "System.Byte" => f.Read<byte>(obj.Address, false),
                                            "System.Single" => float.IsFinite(f.Read<float>(obj.Address, false))
                                                ? f.Read<float>(obj.Address, false) : null,
                                            "System.Boolean" => f.Read<bool>(obj.Address, false),
                                            _ => null
                                        };
                                        return new { f.Name, Value = value };
                                    }
                                    catch (InvalidOperationException) { return new { f.Name, Value = (object?)null }; }
                                }).Where(x => x.Value is not null).ToArray(),
                                RootIndexCandidates = type!.StaticFields.Where(f => f.Type?.Name == "System.Int32" && f.IsInitialized(domain))
                                    .Select(f => new { f.Name, Value = f.Read<int>(domain) })
                                    .Where(x => x.Value == i || x.Value == i + 1).ToArray(),
                                ActorFieldSchema = element.Fields.Select(f => new { f.Name, Type = f.Type?.Name }).ToArray() });
                        }
                    }
                    arrayCounts.Add(new { Array = field.Name, Length = entries.Length, Occupied = occupied, NonEmptyStrings = named });
                }
                catch (InvalidOperationException) { /* Moving data is not evidence. */ }
            }
        }
        using var file = File.OpenRead(Path.Combine(Path.GetDirectoryName(process.MainModule!.FileName)!, "NinOnline.dll"));
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        foreach (var index in matchIndices)
        {
            foreach (var handle in metadata.TypeDefinitions)
            {
                var definition = metadata.GetTypeDefinition(handle);
                if (definition.GetFields().Count < 70 || !definition.GetDeclaringType().IsNil) continue;
                var type = module.GetTypeByName(metadata.GetString(definition.Namespace) + "." + metadata.GetString(definition.Name));
                if (type is null || type.StaticFields.Length < 70) continue;
                foreach (var field in type!.StaticFields.Where(f => f.Type?.Name == "System.Int32" && f.IsInitialized(domain)))
                {
                    try
                    {
                        var value = field.Read<int>(domain);
                        if (value == index || value == index + 1)
                            indexCandidates.Add(new { Type = type.Name, Field = field.Name, Value = value });
                    }
                    catch (InvalidOperationException) { }
                }
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Matches = matches, Arrays = arrayCounts, IndexCandidates = indexCandidates,
            Warning = "Name match is NOT proof of local identity; no profile or input authorized." },
            new JsonSerializerOptions { WriteIndented = true }));
        return matches.Count == 0 ? 1 : 0;
    }

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
