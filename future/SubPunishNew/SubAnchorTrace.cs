using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics; // Archived: not compiled in the shipping diagnostic tool.

/// <summary>Local movement/buff calibration only. Never sends input or treats unknown fields as events.</summary>
public static class SubAnchorTrace
{
    public static int Run(int pid, string character, int seconds)
    {
        if (seconds is < 1 or > 180) throw new ArgumentException("Trace duration 1–180 seconds");
        var clock = new SystemMonotonicClock();
        using var provider = new ReadOnlyGameStateProvider(pid, character, clock);
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        var profile = provider.Profile;
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var watch = Stopwatch.StartNew();
        var first = true;
        ulong rootsTable = 0, globalsTable = 0;
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            provider.GetSnapshot();
            var frame = provider.Frame;
            if (frame is null) { Console.WriteLine(JsonSerializer.Serialize(new { AtMs = watch.ElapsedMilliseconds, provider.Status })); Thread.Sleep(25); continue; }
            runtime.FlushCachedData();
            var roots = rootsTable == 0 ? null : runtime.GetTypeByMethodTable(rootsTable);
            var globals = globalsTable == 0 ? null : runtime.GetTypeByMethodTable(globalsTable);
            if (roots is null || globals is null)
            {
                var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
                roots = module.GetTypeByName(profile.Roots)!;
                globals = module.GetTypeByName(profile.Globals)!;
                rootsTable = roots.MethodTable; globalsTable = globals.MethodTable;
            }
            var domain = runtime.AppDomains.Single();
            var index = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
            var array = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
            if (index <= 0 || index >= array.Length) throw new InvalidOperationException("Local index changed");
            var player = array.GetObjectValue(index);
            if (player.ReadStringField(profile.Name) != character) throw new InvalidOperationException("Identity changed");
            var fields = player.Type!.Fields;
            if (fields.Length != 281 || fields[53].Name != profile.PixelX || fields[54].Name != profile.PixelY)
                throw new InvalidOperationException("Movement schema differs; refusing ordinal calibration");
            if (first)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { Kind = "Schema", profile.Id, Character = character,
                    Warning = "Local movement fields are UNMAPPED candidates; no chat/account strings, no input",
                    Fields = fields.Skip(49).Take(56).Select(f => new { f.Name, Type = f.Type?.Name }) }));
                first = false;
            }
            var values = new Dictionary<string, object?>();
            foreach (var field in fields.Skip(49).Take(56))
            {
                var value = Scalar(player, field);
                if (value is not null) values[field.Name!] = value;
            }
            foreach (var ordinal in new[] { 69, 70, 71, 72, 87 })
            {
                var field = fields[ordinal];
                if (field.Type?.IsValueType == true)
                {
                    var child = player.ReadValueTypeField(field.Name!);
                    foreach (var f in child.Type!.Fields)
                    {
                        var value = Scalar(child, f);
                        if (value is not null) values[$"{field.Name}.{f.Name}"] = value;
                    }
                }
                else
                {
                    var child = player.ReadObjectField(field.Name!);
                    if (child.IsNull) continue;
                    foreach (var f in child.Type!.Fields)
                    {
                        var value = Scalar(child, f);
                        if (value is not null) values[$"{field.Name}.{f.Name}"] = value;
                        if (f.Type?.Name == "System.Numerics.Vector2")
                        {
                            var vector = child.ReadValueTypeField(f.Name!);
                            values[$"{field.Name}.{f.Name}.X"] = vector.ReadField<float>("X");
                            values[$"{field.Name}.{f.Name}.Y"] = vector.ReadField<float>("Y");
                        }
                    }
                }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { Kind = "Sample", AtMs = watch.ElapsedMilliseconds, frame.CapturedAtMs,
                frame.Map, Player = frame.Snapshot.Player, frame.TargetName, Buffs = frame.ObservedActiveBuffs, Values = values }));
            Thread.Sleep(5);
        }
        return 0;
    }

    private static object? Scalar(ClrObject obj, ClrInstanceField f) => f.Type?.Name switch
    {
        "System.Boolean" => obj.ReadField<bool>(f.Name!), "System.Byte" => obj.ReadField<byte>(f.Name!),
        "System.Single" => obj.ReadField<float>(f.Name!), "System.Double" => obj.ReadField<double>(f.Name!),
        "System.Int32" => obj.ReadField<int>(f.Name!), "System.UInt32" => obj.ReadField<uint>(f.Name!),
        "System.Int64" => obj.ReadField<long>(f.Name!), _ => null
    };
    private static object? Scalar(ClrValueType obj, ClrInstanceField f) => f.Type?.Name switch
    {
        "System.Boolean" => obj.ReadField<bool>(f.Name!), "System.Byte" => obj.ReadField<byte>(f.Name!),
        "System.Single" => obj.ReadField<float>(f.Name!), "System.Double" => obj.ReadField<double>(f.Name!),
        "System.Int32" => obj.ReadField<int>(f.Name!), "System.UInt32" => obj.ReadField<uint>(f.Name!),
        "System.Int64" => obj.ReadField<long>(f.Name!), _ => null
    };
}
