using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Scoped, read-only numeric fields of one named player slot. Candidate values are NOT mapped coordinates.</summary>
public static class ActorCoordinateSurvey
{
    public static int Run(int pid, string character, string actorName, int slot, string manifest, int seconds)
    {
        if (seconds is < 5 or > 60 || slot <= 0 || actorName.Length is < 1 or > 64)
            throw new ArgumentException("Explicit actor and bounded capture required");
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
        var directory = Path.GetDirectoryName(process.MainModule?.FileName)!;
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        var profile = SignedProfileCatalog.VerifyAndSelect(File.ReadAllBytes(manifest),
            Convert.FromBase64String(File.ReadAllText(manifest + ".sig").Trim()), SignedProfileCatalog.PublicKey,
            Hash(Path.Combine(directory, "NinOnline.dll")), Hash(Path.Combine(directory, "Common.dll")))
            ?? throw new InvalidOperationException("No exact-pair signed profile");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var observations = new List<object>();
        int failures = 0;
        while (clock.ElapsedMilliseconds < seconds * 1000L)
        {
            try
            {
                runtime.FlushCachedData();
                var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
                var domain = runtime.AppDomains.Single();
                var roots = module.GetTypeByName(profile.Roots)!;
                var globals = module.GetTypeByName(profile.Globals)!;
                var localIndex = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
                var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
                if (slot >= players.Length || localIndex <= 0 || localIndex >= players.Length)
                    throw new InvalidOperationException("Player slot changed");
                var local = players.GetObjectValue(localIndex);
                var actor = players.GetObjectValue(slot);
                if (local.IsNull || local.ReadStringField(profile.Name, 64) != character ||
                    actor.IsNull || actor.ReadStringField(profile.Name, 64) != actorName)
                    throw new InvalidOperationException("Actor identity changed; no stale slot reuse");
                var values = new Dictionary<string, double>();
                foreach (var field in actor.Type!.Fields.Where(f => f.Type?.Name is "System.Single" or "System.Double" or "System.Int32"))
                {
                    try
                    {
                        var value = field.Type!.Name switch
                        {
                            "System.Single" => (double)field.Read<float>(actor.Address, false),
                            "System.Double" => field.Read<double>(actor.Address, false),
                            _ => (double)field.Read<int>(actor.Address, false)
                        };
                        if (double.IsFinite(value) && Math.Abs(value) <= 100_000) values[field.Name!] = value;
                    }
                    catch { /* Never fill in unreadable values from another sample. */ }
                }
                if (players.GetObjectValue(slot).Address != actor.Address ||
                    actor.ReadStringField(profile.Name, 64) != actorName ||
                    globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain) != localIndex)
                    throw new InvalidOperationException("Player array changed during read");
                observations.Add(new { AtMs = clock.ElapsedMilliseconds, Slot = slot, Name = actorName,
                    LocalName = character, Values = values });
            }
            catch { failures++; }
            Thread.Sleep(120);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Samples = observations, Failures = failures,
            Warning = "Candidate numeric fields only. No proof of hidden player position, camera or decoy identity; no input." }));
        return observations.Count > 0 ? 0 : 1;
    }
}
