using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Captures changing primitive candidates only; never uses the input adapter.</summary>
public static class PassiveCastSurvey
{
    public static int Run(int pid, string character, string profilePath, int seconds)
    {
        if (seconds is < 10 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        var profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(profilePath))
            ?? throw new InvalidOperationException("Missing read-only profile");
        if (profile.ActuationValidated || profile.ValidatedInputKinds.Length != 0 ||
            profile.ValidatedAttackModes.Length != 0 || !profile.ExternalProfile)
            throw new InvalidOperationException("Survey accepts only input-disabled local calibration");
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline");
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var game = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
        using (var common = File.OpenRead(Path.Combine(directory, "Common.dll")))
            if (Convert.ToHexString(SHA256.HashData(game)) != profile.Sha256 ||
                Convert.ToHexString(SHA256.HashData(common)) != profile.CommonSha256)
                throw new InvalidOperationException("Game build changed");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var history = new List<object>();
        Dictionary<string, long>? previous = null;
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            runtime.FlushCachedData();
            var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
            var domain = runtime.AppDomains.Single();
            var roots = module.GetTypeByName(profile.Roots)!;
            var globals = module.GetTypeByName(profile.Globals)!;
            var index = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
            var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
            if (index <= 0 || index >= players.Length) throw new InvalidOperationException("Local index unavailable");
            var actor = players.GetObjectValue(index);
            if (actor.IsNull || actor.ReadStringField(profile.Name, 64) != character)
                throw new InvalidOperationException("Local character changed");
            var current = new Dictionary<string, long>();
            foreach (var f in globals.StaticFields.Where(f => f.IsInitialized(domain) &&
                f.Type?.Name is "System.Int32" or "System.Int64"))
                try { current["G:" + f.Name] = f.Type!.Name == "System.Int64"
                    ? f.Read<long>(domain) : f.Read<int>(domain); } catch { }
            foreach (var f in actor.Type!.Fields.Where(f => f.Type?.Name == "System.Int32"))
                try { current["A:" + f.Name] = f.Read<int>(actor.Address, false); } catch { }
            var changed = previous is null ? current : current.Where(x =>
                !previous.TryGetValue(x.Key, out var old) || old != x.Value)
                .ToDictionary(x => x.Key, x => x.Value);
            if (changed.Count > 0) history.Add(new { AtMs = watch.ElapsedMilliseconds, Index = index, Changed = changed });
            previous = current;
            Thread.Sleep(80);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Samples = history,
            Warning = "Primitive transitions only; no cast semantics or input approved." }));
        return 0;
    }
}
