using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive, exact-build buff candidate transitions. No semantic mapping or input.</summary>
public static class RawBuffWatch
{
    public static int Run(int pid, string character, string profilePath, int seconds)
    {
        if (seconds is < 10 or > 60 || string.IsNullOrWhiteSpace(character)) return 2;
        var profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(profilePath))
            ?? throw new InvalidOperationException("Read-only profile unavailable");
        if (!profile.ExternalProfile || profile.ActuationValidated || profile.ValidatedInputKinds.Length != 0 ||
            profile.ValidatedAttackModes.Length != 0 || profile.PunishComboValidated != false)
            throw new InvalidOperationException("Survey requires input-disabled profile");
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") return 2;
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var game = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
            if (!Convert.ToHexString(SHA256.HashData(game)).Equals(profile.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game DLL changed during survey");
        using (var common = File.OpenRead(Path.Combine(directory, "Common.dll")))
            if (!Convert.ToHexString(SHA256.HashData(common)).Equals(profile.CommonSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Common DLL changed during survey");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var roots = module.GetTypeByName(profile.Roots)!;
        var globals = module.GetTypeByName(profile.Globals)!;
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
                int slot = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
                var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
                if (slot <= 0 || slot >= players.Length) throw new InvalidOperationException("Local slot invalid");
                var actor = players.GetObjectValue(slot);
                if (actor.IsNull || actor.ReadStringField(profile.Name, 64) != character)
                    throw new InvalidOperationException("Character changed");
                var buffs = actor.ReadObjectField(profile.Buffs);
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
                    actor.ReadStringField(profile.Name, 64) != character)
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
