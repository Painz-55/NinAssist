using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Calibration evidence only; field semantics are not inferred from values.</summary>
public static class StateProbe
{
    public const string RootType = "ElTfx1eaUWfDotOKOf4A.N3S6pdea9c38MU1PQPYi";
    public const string PlayerArrayField = "vXFeaSMlkle";
    public const string GlobalsType = "YZGTHnmxd6HttrIwHLAt.IeAbl1m3zu618JKfosQ9";

    public static int Run(int pid, bool compact = false)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
            using var target = DataTarget.AttachToProcess(pid, suspend: false);
            using var runtime = target.ClrVersions.Single().CreateRuntime();
            var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
            var domain = runtime.AppDomains.Single();
            var root = module.GetTypeByName(RootType) ?? throw new InvalidOperationException("Root type unavailable");
            var array = root.GetStaticFieldByName(PlayerArrayField)?.ReadObject(domain) ?? default;
            var players = new List<object>();
            if (!array.IsNull && array.IsArray)
            {
                var entries = array.AsArray();
                for (var i = 0; i < Math.Min(entries.Length, 2000); i++)
                {
                    var obj = entries.GetObjectValue(i);
                    if (obj.IsNull) continue;
                    var name = obj.ReadStringField("gwMAu0vTEek", 64);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var fields = new Dictionary<string, object?>();
                    foreach (var field in obj.Type!.Fields)
                    {
                        var value = ReadPrimitive(field.Type?.Name, () => field.Read<int>(obj.Address, false),
                            () => field.Read<byte>(obj.Address, false), () => field.Read<float>(obj.Address, false),
                            () => field.Read<double>(obj.Address, false), () => field.Read<bool>(obj.Address, false));
                        if (value is not null) fields[field.Name!] = value;
                    }
                    foreach (var field in obj.Type.Fields.Where(f =>
                        (i == module.GetTypeByName(GlobalsType)!.GetStaticFieldByName("eOom5HvkyBm")!.Read<int>(domain)
                            && f.Type?.Name is "System.Int32[]" or "System.Byte[]") || f.Name is "MXIA5Hxi0Pe" or "uGiA58yEnh9"))
                        {
                            var a = field.ReadObject(obj.Address, false);
                            if (a.IsNull || !a.IsArray) continue;
                            var ar = a.AsArray();
                            if (ar.Length > 256) continue;
                            fields[field.Name!] = field.Type!.Name == "System.Int32[]"
                                ? ar.ReadValues<int>(0, ar.Length) : ar.ReadValues<byte>(0, ar.Length);
                        }
                    if (i == module.GetTypeByName(GlobalsType)!.GetStaticFieldByName("eOom5HvkyBm")!.Read<int>(domain))
                    {
                        var buffsObject = obj.ReadObjectField("NbdAlOX7FNP");
                        var buffs = new List<object>();
                        if (!buffsObject.IsNull && buffsObject.IsArray)
                        {
                            var ar = buffsObject.AsArray();
                            for (var j = 0; j < Math.Min(ar.Length, 256); j++)
                            {
                                var buff = ar.GetObjectValue(j);
                                if (buff.IsNull) continue;
                                var values = new Dictionary<string, object?>();
                                foreach (var f in buff.Type!.Fields.Where(f => f.Type?.Name == "System.Int32"))
                                    values[f.Name!] = f.Read<int>(buff.Address, false);
                                buffs.Add(new { Slot = j, Name = buff.ReadStringField("cPyA1N8efbj", 128), Fields = values });
                            }
                        }
                        fields["BuffCandidates"] = buffs;
                    }
                    players.Add(new { Index = i, Name = name, Address = $"0x{obj.Address:X}", Fields = fields });
                }
            }
            var globals = module.GetTypeByName(GlobalsType);
            var numbers = new Dictionary<string, object?>();
            if (globals is not null)
                foreach (var field in globals.StaticFields)
                {
                    if (!field.IsInitialized(domain) || field.GetAddress(domain) == 0) continue;
                    var value = ReadPrimitive(field.Type?.Name, () => field.Read<int>(domain),
                        () => field.Read<byte>(domain), () => field.Read<float>(domain),
                        () => field.Read<double>(domain), () => field.Read<bool>(domain));
                    if (value is not null) numbers[field.Name!] = value;
                }
            Console.WriteLine(JsonSerializer.Serialize(new { ProcessId = pid, ObservedAtUtc = DateTimeOffset.UtcNow,
                Warning = "Calibration only; numeric fields are unmapped. No chat/account strings collected.",
                PlayerArrayAvailable = !array.IsNull,
                RootField = new { Initialized = root.GetStaticFieldByName(PlayerArrayField)?.IsInitialized(domain),
                    Address = root.GetStaticFieldByName(PlayerArrayField)?.GetAddress(domain) },
                Players = players, Globals = numbers },
                new JsonSerializerOptions { WriteIndented = !compact }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static object? ReadPrimitive(string? type, Func<int> integer, Func<byte> b,
        Func<float> f, Func<double> d, Func<bool> boolean) => type switch
    {
        "System.Int32" => integer(), "System.Byte" => b(), "System.Boolean" => boolean(),
        "System.Single" => float.IsFinite(f()) ? f() : null,
        "System.Double" => double.IsFinite(d()) ? d() : null,
        _ => null
    };
}
