using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive numeric transitions for an explicitly identified actor on an exact game build.</summary>
public static class RawActorMovementWatch
{
    public static int Run(int pid, string character, string expectedHash, int seconds)
    {
        if (seconds is < 1 or > 60 || string.IsNullOrWhiteSpace(character)) return 2;
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") return 2;
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var game = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
            if (!Convert.ToHexString(SHA256.HashData(game)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game DLL changed during survey");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var root = module.GetTypeByName("U8pNKXwmDywLBlb4iEQ1.dINrwIwmKXlj02ux6uZS")!;
        var globals = module.GetTypeByName("FmDTqE8LkU93SDNCri97.Y5Qgfu8Lix72p7FJ0EHF")!;
        var changes = new List<object>();
        var previous = new Dictionary<string, double>();
        var count = 0;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            runtime.FlushCachedData();
            var domain = runtime.AppDomains.Single();
            int slot = globals.GetStaticFieldByName("AID86bUe12D")!.Read<int>(domain);
            var players = root.GetStaticFieldByName("IEVwmvVWoet")!.ReadObject(domain).AsArray();
            if (slot <= 0 || slot >= players.Length) throw new InvalidOperationException("Local slot invalid");
            var actor = players.GetObjectValue(slot);
            if (actor.IsNull || actor.ReadStringField("K337309iOKN", 64) != character)
                throw new InvalidOperationException("Local character changed");
            var current = new Dictionary<string, double>();
            foreach (var field in actor.Type!.Fields)
            {
                try
                {
                    double value = field.Type?.Name switch
                    {
                        "System.Single" => field.Read<float>(actor.Address, false),
                        "System.Double" => field.Read<double>(actor.Address, false),
                        "System.Int32" => field.Read<int>(actor.Address, false),
                        "System.Byte" => field.Read<byte>(actor.Address, false),
                        "System.Boolean" => field.Read<bool>(actor.Address, false) ? 1 : 0,
                        _ => double.NaN
                    };
                    if (double.IsFinite(value) && Math.Abs(value) < 10_000_000) current[field.Name!] = value;
                }
                catch { /* An unreadable field is never supplied from a prior sample. */ }
            }
            if (players.GetObjectValue(slot).Address != actor.Address ||
                actor.ReadStringField("K337309iOKN", 64) != character)
                throw new InvalidOperationException("Local player changed mid-read");
            var changed = current.Where(kv => !previous.TryGetValue(kv.Key, out double old) || old != kv.Value)
                .Select(kv => new { Field = kv.Key, Value = kv.Value }).ToArray();
            if (changed.Length > 0) changes.Add(new { AtMs = watch.ElapsedMilliseconds, Fields = changed });
            previous = current;
            count++;
            Thread.Sleep(120);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Samples = count, Changes = changes,
            Warning = "Candidate numeric transitions only. No coordinate/HP meaning or input approved." }));
        return 0;
    }
}
