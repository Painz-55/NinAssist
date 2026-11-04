using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only calibration: scalar candidates are NOT a validated cast signal.</summary>
public static class CastStateTrace
{
    private static bool Scalar(string? type) => type is "System.Boolean" or "System.Byte" or "System.Int16" or
        "System.Int32" or "System.UInt32" or "System.Int64" or "System.Single" or "System.Double";

    public static int Run(int pid, string character, int seconds)
    {
        if (seconds is < 5 or > 180) throw new ArgumentOutOfRangeException(nameof(seconds));
        using var provider = new ReadOnlyGameStateProvider(pid, character, new SystemMonotonicClock());
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        var profile = provider.Profile;
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var rootsId = module.GetTypeByName(profile.Roots)!.MethodTable;
        var globalsId = module.GetTypeByName(profile.Globals)!.MethodTable;
        var watch = Stopwatch.StartNew();
        var last = new Dictionary<string, object>();
        Console.WriteLine(JsonSerializer.Serialize(new { Kind = "Header", profile.Id, profile.Sha256, profile.CommonSha256,
            Character = character, Seconds = seconds, Warning = "Calibration only: cast fields not yet identified. No input sent." }));
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            var atMs = watch.ElapsedMilliseconds;
            try
            {
                runtime.FlushCachedData();
                var roots = runtime.GetTypeByMethodTable(rootsId) ?? throw new InvalidOperationException("Roots unavailable");
                var globals = runtime.GetTypeByMethodTable(globalsId) ?? throw new InvalidOperationException("Globals unavailable");
                var domain = runtime.AppDomains.Single();
                var local = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
                var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
                if (local <= 0 || local >= players.Length) throw new InvalidOperationException("Local unavailable");
                var player = players.GetObjectValue(local);
                if (player.IsNull || player.ReadStringField(profile.Name, 64) != character)
                    throw new InvalidOperationException("Local identity changed");
                var values = new Dictionary<string, object>();
                foreach (var field in player.Type!.Fields.Where(f => Scalar(f.Type?.Name)))
                {
                    object value = field.Type!.Name switch
                    {
                        "System.Boolean" => field.Read<bool>(player.Address, false),
                        "System.Byte" => field.Read<byte>(player.Address, false),
                        "System.Int16" => field.Read<short>(player.Address, false),
                        "System.Int32" => field.Read<int>(player.Address, false),
                        "System.UInt32" => field.Read<uint>(player.Address, false),
                        "System.Int64" => field.Read<long>(player.Address, false),
                        "System.Single" => field.Read<float>(player.Address, false),
                        _ => field.Read<double>(player.Address, false)
                    };
                    if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d)) continue;
                    values["Player." + field.Name] = value;
                }
                foreach (var type in new[] { roots, globals })
                foreach (var field in type.StaticFields.Where(f => Scalar(f.Type?.Name) && f.IsInitialized(domain)))
                {
                    object value = field.Type!.Name switch
                    {
                        "System.Boolean" => field.Read<bool>(domain), "System.Byte" => field.Read<byte>(domain),
                        "System.Int16" => field.Read<short>(domain), "System.Int32" => field.Read<int>(domain),
                        "System.UInt32" => field.Read<uint>(domain), "System.Int64" => field.Read<long>(domain),
                        "System.Single" => field.Read<float>(domain), _ => field.Read<double>(domain)
                    };
                    if (value is float f && !float.IsFinite(f) || value is double d && !double.IsFinite(d)) continue;
                    values[(type.MethodTable == rootsId ? "Roots." : "Globals.") + field.Name] = value;
                }
                if (globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain) != local ||
                    players.GetObjectValue(local).Address != player.Address || player.ReadStringField(profile.Name, 64) != character)
                    throw new InvalidOperationException("Local changed during read");
                var changes = values.Where(pair => !last.TryGetValue(pair.Key, out var old) || !Equals(pair.Value, old))
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                if (changes.Count > 0)
                    Console.WriteLine(JsonSerializer.Serialize(new { Kind = "Sample", AtMs = atMs,
                        ReadMs = watch.ElapsedMilliseconds - atMs, Map = player.ReadField<int>(profile.Map), Changes = changes }));
                last = values;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                last.Clear();
                Console.WriteLine(JsonSerializer.Serialize(new { Kind = "Discarded", AtMs = atMs, Reason = ex.Message }));
            }
            Thread.Sleep(20);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Kind = "End", AtMs = watch.ElapsedMilliseconds }));
        return 0;
    }
}
