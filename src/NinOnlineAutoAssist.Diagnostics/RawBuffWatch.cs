using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive, exact-build buff candidate transitions. No semantic mapping or input.</summary>
public static class RawBuffWatch
{
    public static int Run(int pid, string character, string expectedHash, int seconds)
    {
        if (seconds is < 10 or > 60 || string.IsNullOrWhiteSpace(character)) return 2;
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") return 2;
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var game = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
            if (!Convert.ToHexString(SHA256.HashData(game)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game DLL changed during survey");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var roots = module.GetTypeByName("U8pNKXwmDywLBlb4iEQ1.dINrwIwmKXlj02ux6uZS")!;
        var globals = module.GetTypeByName("FmDTqE8LkU93SDNCri97.Y5Qgfu8Lix72p7FJ0EHF")!;
        var observations = new List<object>();
        var failures = 0;
        string? previous = null;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            try
            {
                runtime.FlushCachedData();
                var domain = runtime.AppDomains.Single();
                int slot = globals.GetStaticFieldByName("AID86bUe12D")!.Read<int>(domain);
                var players = roots.GetStaticFieldByName("IEVwmvVWoet")!.ReadObject(domain).AsArray();
                if (slot <= 0 || slot >= players.Length) throw new InvalidOperationException("Local slot invalid");
                var actor = players.GetObjectValue(slot);
                if (actor.IsNull || actor.ReadStringField("K337309iOKN", 64) != character)
                    throw new InvalidOperationException("Character changed");
                var buffs = actor.ReadObjectField("A1P7YKl9o8d");
                if (!buffs.IsArray || buffs.AsArray().Length != 11)
                    throw new InvalidOperationException("Buff candidate layout changed");
                var entries = new List<object>();
                for (int i = 0; i < buffs.AsArray().Length; i++)
                {
                    var entry = buffs.AsArray().GetObjectValue(i);
                    if (entry.IsNull) continue;
                    var strings = entry.Type!.Fields.Where(f => f.Type?.Name == "System.String")
                        .Select(f => new { f.Name, Value = entry.ReadStringField(f.Name!, 128) }).ToArray();
                    var numbers = entry.Type.Fields.Where(f => f.Type?.Name == "System.Int32")
                        .Select(f => new { f.Name, Value = f.Read<int>(entry.Address, false) }).ToArray();
                    var flags = entry.Type.Fields.Where(f => f.Type?.Name == "System.Boolean")
                        .Select(f => new { f.Name, Value = f.Read<bool>(entry.Address, false) }).ToArray();
                    if (strings.Any(s => !string.IsNullOrWhiteSpace(s.Value)) || numbers.Any(n => n.Value != 0) || flags.Any(f => f.Value))
                        entries.Add(new { Slot = i, Strings = strings, Numbers = numbers, Flags = flags });
                }
                if (players.GetObjectValue(slot).Address != actor.Address ||
                    actor.ReadStringField("K337309iOKN", 64) != character)
                    throw new InvalidOperationException("Character changed during read");
                var marker = JsonSerializer.Serialize(entries);
                if (marker != previous)
                {
                    observations.Add(new { AtMs = watch.ElapsedMilliseconds, Entries = entries });
                    previous = marker;
                }
            }
            catch (InvalidOperationException) { failures++; }
            Thread.Sleep(150);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Observations = observations, Failures = failures,
            Warning = "Read-only candidate values; manual visual buff confirmation required. No input approved." }));
        return observations.Count > 0 ? 0 : 1;
    }
}
