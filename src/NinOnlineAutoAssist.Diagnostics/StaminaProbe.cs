using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

public static class StaminaProbe
{
    public static int Run(int pid, string character)
    {
        using var provider = new ReadOnlyGameStateProvider(pid, character, new SystemMonotonicClock());
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var profile = provider.Profile;
        var roots = module.GetTypeByName(profile.Roots)!;
        var globals = module.GetTypeByName(profile.Globals)!;
        var domain = runtime.AppDomains.Single();
        var index = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
        var local = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray().GetObjectValue(index);
        if (local.ReadStringField(profile.Name) != character) throw new InvalidOperationException("Identity changed");
        object? Value(ClrObject obj, ClrInstanceField f) => f.Type?.Name switch
        {
            "System.Boolean" => obj.ReadField<bool>(f.Name!),
            "System.Single" => obj.ReadField<float>(f.Name!),
            "System.Double" => obj.ReadField<double>(f.Name!),
            "System.Int32" => obj.ReadField<int>(f.Name!),
            "System.Int64" => obj.ReadField<long>(f.Name!),
            "System.Int32[]" => obj.ReadObjectField(f.Name!).AsArray().ReadValues<int>(0, obj.ReadObjectField(f.Name!).AsArray().Length),
            _ => null
        };
        var nested = new List<object>();
        var flags = new Dictionary<string, object>();
        foreach (var type in new[] { roots, globals })
            foreach (var f in type.StaticFields.Where(f => f.IsInitialized(domain)))
            {
                if (f.Type?.Name == "System.Boolean") flags[$"{type.Name}.{f.Name}"] = f.Read<bool>(domain);
                if (f.Type?.Name == "System.Byte") flags[$"{type.Name}.{f.Name}"] = f.Read<byte>(domain);
            }
        foreach (var f in local.Type!.Fields.Where(f => f.Name == "ueQAbfQV5bF"))
        {
            var obj = local.ReadObjectField(f.Name!);
            if (!obj.IsNull) nested.Add(new { f.Name, Type = obj.Type!.Name,
                Fields = obj.Type.Fields.Select(n => new { n.Name, Type = n.Type?.Name, Value = Value(obj, n) }).ToArray() });
        }
        using var stream = File.OpenRead(Path.Combine(provider.GameDirectory, "Common.dll"));
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var vitals = md.TypeDefinitions.Select(h => md.GetTypeDefinition(h)).Where(t => md.GetString(t.Name) == "Vitals")
            .SelectMany(t => t.GetFields()).Select(h => md.GetFieldDefinition(h)).Where(f => !f.GetDefaultValue().IsNil)
            .Select(f => new { Name = md.GetString(f.Name), Hex = Convert.ToHexString(md.GetBlobBytes(md.GetConstant(f.GetDefaultValue()).Value)) }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { provider.Status, Vitals = vitals, Flags = flags,
            Fields = local.Type.Fields.Select(f => new { f.Name, Type = f.Type?.Name, Value = Value(local, f) }).ToArray(), Nested = nested },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
