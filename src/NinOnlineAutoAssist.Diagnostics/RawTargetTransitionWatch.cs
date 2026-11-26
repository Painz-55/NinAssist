using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only transitions in candidate target objects; never infers or writes a target.</summary>
public static class RawTargetTransitionWatch
{
    public static int Run(int pid, string character, string expectedHash, int seconds)
    {
        if (seconds is < 10 or > 60 || string.IsNullOrWhiteSpace(character)) return 2;
        GameMemoryProfile? profile = null;
        if (File.Exists(expectedHash))
        {
            profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(expectedHash))
                ?? throw new InvalidOperationException("Read-only profile unavailable");
            if (!profile.ExternalProfile || profile.ActuationValidated || profile.ExperimentalTrial ||
                profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0 ||
                profile.PunishComboValidated != false || profile.SelectedTarget is not null)
                throw new InvalidOperationException("Survey requires input-disabled candidate profile");
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
        var fields = profile is null ? new[] { "USB8ptWfm1N", "s188pQftV3T" } : new[] { "YPDrG8hKA8g" };
        var observations = new List<object>();
        string? prior = null;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            runtime.FlushCachedData();
            var domain = runtime.AppDomains.Single();
            var slot = globals.GetStaticFieldByName(profile?.LocalIndex ?? "AID86bUe12D")!.Read<int>(domain);
            var players = root.GetStaticFieldByName(profile?.Players ?? "IEVwmvVWoet")!.ReadObject(domain).AsArray();
            if (slot <= 0 || slot >= players.Length ||
                players.GetObjectValue(slot).ReadStringField(profile?.Name ?? "K337309iOKN", 64) != character)
                throw new InvalidOperationException("Local identity changed during survey");
            var states = fields.Select(name =>
            {
                var obj = globals.GetStaticFieldByName(name)!.ReadObject(domain);
                return new { Name = name, IsNull = obj.IsNull,
                    Values = obj.IsNull ? null : obj.Type!.Fields.Where(f => f.Type?.Name == "System.Int32")
                        .Select(f => new { f.Name, Value = f.Read<int>(obj.Address, false) }).ToArray() };
            }).ToArray();
            var marker = JsonSerializer.Serialize(states);
            if (marker != prior)
            {
                observations.Add(new { AtMs = watch.ElapsedMilliseconds, Slot = slot, States = states });
                prior = marker;
            }
            if (globals.GetStaticFieldByName(profile?.LocalIndex ?? "AID86bUe12D")!.Read<int>(domain) != slot ||
                players.GetObjectValue(slot).ReadStringField(profile?.Name ?? "K337309iOKN", 64) != character)
                throw new InvalidOperationException("Local identity changed during survey");
            Thread.Sleep(100);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Observations = observations,
            Warning = "Read-only transitions; manual player/NPC selection needs visual confirmation. No input approved." }));
        return 0;
    }
}
