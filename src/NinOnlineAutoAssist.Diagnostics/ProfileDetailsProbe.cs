using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Scoped, passive schema observations for the exact 2026-11-06 DLL; never approves actions.</summary>
public static class ProfileDetailsProbe
{
    public static int Run(int pid, string character)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || character.Length is < 1 or > 64)
            throw new InvalidOperationException("Expected NinOnline and explicit character");
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var file = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
            if (Convert.ToHexString(SHA256.HashData(file)) != "B384E2E1EE84A5AC0CB86AAE4F9A4A0A9D41C4F3E68195F9AC434656C9B05A60")
                throw new InvalidOperationException("Build changed; refusing stale field names");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        runtime.FlushCachedData();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var domain = runtime.AppDomains.Single();
        var roots = module.GetTypeByName("oRGErqTKUmEPMiRFsMdt.ILqvIRTizh9luQa4ZLwE")!;
        var globals = module.GetTypeByName("IsjDw5pRqGrqMy0QsTjL.a8qup8pRkLSjt8n8JXZm")!;
        var index = globals.GetStaticFieldByName("ym6pBQ1rIAI")!.Read<int>(domain);
        var actor = roots.GetStaticFieldByName("ApvTK57RlqR")!.ReadObject(domain).AsArray().GetObjectValue(index);
        if (actor.IsNull || actor.ReadStringField("VJ0Sp4nQmlR", 64) != character)
            throw new InvalidOperationException("Local identity changed");
        var arrays = actor.Type!.Fields.Where(f => f.Type?.IsArray == true).Select(f =>
        {
            try
            {
                var obj = f.ReadObject(actor.Address, false);
                var component = f.Type?.ComponentType;
                return new { f.Name, Type = f.Type?.Name, Length = obj.IsArray ? obj.AsArray().Length : -1,
                    Component = component?.Name,
                    ComponentFields = component?.Fields.Select(c => new { c.Name, Type = c.Type?.Name }).ToArray() };
            }
            catch { return null; }
        }).Where(x => x is not null && x.Length is >= 1 and <= 256 &&
            x.ComponentFields?.Any(f => f.Type == "System.String") == true).ToArray();
        var map = roots.GetStaticFieldByName("DLJTKfdDu0m")!.ReadObject(domain);
        var targetField = globals.GetStaticFieldByName("t0vpBYkcQwH")!;
        var selected = targetField.ReadObject(domain);
        object? Ints(ClrObject obj) => obj.IsNull ? null : obj.Type?.Fields.Where(f => f.Type?.Name == "System.Int32")
            .Select(f => new { f.Name, Value = f.Read<int>(obj.Address, false) }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            LocalIndex = index,
            ActorMap = actor.ReadField<int>("tm7SL3k18JZ"),
            MapIdentity = roots.GetStaticFieldByName("PxWTKIT5b2S")!.Read<int>(domain),
            MapObjectType = map.Type?.Name,
            MapFields = map.IsNull ? null : map.Type?.Fields.Where(f => f.Type?.Name == "Common.TileRec[,]" || f.Type?.Name == "System.Int32")
                .Select(f => new { f.Name, Type = f.Type?.Name }).ToArray(),
            TargetType = targetField.Type?.Name, SelectedTargetIsNull = selected.IsNull,
            TargetFields = targetField.Type?.Fields.Where(f => f.Type?.Name == "System.Int32")
                .Select(f => new { f.Name, Type = f.Type?.Name }).ToArray(), TargetValues = Ints(selected),
            ActorArrayCandidates = arrays,
            ProjectileFields = roots.StaticFields.Where(f => f.Type?.Name == "client.ProjectileRec[]")
                .Select(f => f.Name).ToArray(),
            Warning = "Read-only candidates; no typed target or buff semantics approved."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
