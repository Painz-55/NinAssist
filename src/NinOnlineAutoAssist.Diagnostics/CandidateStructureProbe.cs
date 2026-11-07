using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only inspection of explicitly supplied schema candidates; no profile or input approval.</summary>
public static class CandidateStructureProbe
{
    public static int Run(int pid, string character, string rootsName, string globalsName,
        string playersName, string indexName, string nameField, string buffsName, string targetName, string mapName)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || string.IsNullOrWhiteSpace(character))
            throw new InvalidOperationException("Expected NinOnline and explicit character");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var domain = runtime.AppDomains.Single();
        var roots = module.GetTypeByName(rootsName) ?? throw new InvalidOperationException("Root type not found");
        var globals = module.GetTypeByName(globalsName) ?? throw new InvalidOperationException("Global type not found");
        var index = globals.GetStaticFieldByName(indexName)?.Read<int>(domain) ?? -1;
        var players = roots.GetStaticFieldByName(playersName)?.ReadObject(domain);
        if (players is not { IsArray: true } || index <= 0 || index >= players.Value.AsArray().Length)
            throw new InvalidOperationException("Local index/array unavailable");
        var actor = players.Value.AsArray().GetObjectValue(index);
        if (actor.IsNull || actor.ReadStringField(nameField, 64) != character)
            throw new InvalidOperationException("Local index does not match confirmed character");
        static object? Fields(ClrType? type) => type?.Fields.Select(f => new { f.Name, Type = f.Type?.Name }).ToArray();
        var buff = actor.ReadObjectField(buffsName);
        var selected = globals.GetStaticFieldByName(targetName)?.ReadObject(domain);
        var map = roots.GetStaticFieldByName(mapName)?.ReadObject(domain);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            LocalIndex = index, BuffArrayLength = buff.IsArray ? buff.AsArray().Length : -1,
            BuffFields = Fields(buff.Type?.ComponentType),
            BuffEntries = buff.IsArray ? Enumerable.Range(0, Math.Min(buff.AsArray().Length, 256))
                .Select(i => buff.AsArray().GetObjectValue(i)).Where(o => !o.IsNull)
                .Select(o => new { Strings = o.Type?.Fields.Where(f => f.Type?.Name == "System.String")
                        .Select(f => new { f.Name, Value = o.ReadStringField(f.Name!, 80) }).ToArray(),
                    Integers = o.Type?.Fields.Where(f => f.Type?.Name == "System.Int32")
                        .Select(f => new { f.Name, Value = f.Read<int>(o.Address, false) }).ToArray() }).ToArray() : null,
            SelectedIsNull = selected is null || selected.Value.IsNull,
            SelectedFields = Fields(selected?.Type),
            SelectedIntegers = selected is { IsNull: false } obj ? obj.Type?.Fields
                .Where(f => f.Type?.Name == "System.Int32")
                .Select(f => new { f.Name, Value = f.Read<int>(obj.Address, false) }).ToArray() : null,
            MapFields = Fields(map?.Type),
            Warning = "Candidates only; no target/buff semantics or input approved."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
