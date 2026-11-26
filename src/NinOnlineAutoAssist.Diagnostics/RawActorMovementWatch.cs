using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive numeric transitions for an explicitly identified actor on an exact game build.</summary>
public static class RawActorMovementWatch
{
    public static int Run(int pid, string character, string expectedHash, int seconds)
    {
        if (seconds is < 1 or > 60 || string.IsNullOrWhiteSpace(character)) return 2;
        GameMemoryProfile? profile = null;
        if (File.Exists(expectedHash))
        {
            profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(expectedHash))
                ?? throw new InvalidOperationException("Read-only profile unavailable");
            if (!profile.ExternalProfile || profile.ActuationValidated || profile.ExperimentalTrial ||
                profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0 ||
                profile.PunishComboValidated != false)
                throw new InvalidOperationException("Survey requires input-disabled profile");
            expectedHash = profile.Sha256;
        }
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") return 2;
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var game = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
            if (!Convert.ToHexString(SHA256.HashData(game)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game DLL changed during survey");
        if (profile is not null)
            using (var common = File.OpenRead(Path.Combine(directory, "Common.dll")))
                if (!Convert.ToHexString(SHA256.HashData(common)).Equals(profile.CommonSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Common DLL changed during survey");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var root = module.GetTypeByName(profile?.Roots ?? "U8pNKXwmDywLBlb4iEQ1.dINrwIwmKXlj02ux6uZS")!;
        var globals = module.GetTypeByName(profile?.Globals ?? "FmDTqE8LkU93SDNCri97.Y5Qgfu8Lix72p7FJ0EHF")!;
        var changes = new List<object>();
        var previous = new Dictionary<string, double>();
        var count = 0;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            runtime.FlushCachedData();
            var domain = runtime.AppDomains.Single();
            int slot = globals.GetStaticFieldByName(profile?.LocalIndex ?? "AID86bUe12D")!.Read<int>(domain);
            var players = root.GetStaticFieldByName(profile?.Players ?? "IEVwmvVWoet")!.ReadObject(domain).AsArray();
            if (slot <= 0 || slot >= players.Length) throw new InvalidOperationException("Local slot invalid");
            var actor = players.GetObjectValue(slot);
            if (actor.IsNull || actor.ReadStringField(profile?.Name ?? "K337309iOKN", 64) != character)
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
                actor.ReadStringField(profile?.Name ?? "K337309iOKN", 64) != character ||
                globals.GetStaticFieldByName(profile?.LocalIndex ?? "AID86bUe12D")!.Read<int>(domain) != slot)
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
