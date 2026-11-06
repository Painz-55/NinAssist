using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only correlation of cast candidates, never casts or approves an action.</summary>
public static class CastCandidateProbe
{
    public static int Run(int pid, string character, int seconds = 30)
    {
        if (seconds is < 10 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || character.Length is < 1 or > 64)
            throw new InvalidOperationException("Expected NinOnline and explicit character name");
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        using (var file = File.OpenRead(Path.Combine(directory, "NinOnline.dll")))
            if (Convert.ToHexString(SHA256.HashData(file)) != "B384E2E1EE84A5AC0CB86AAE4F9A4A0A9D41C4F3E68195F9AC434656C9B05A60")
                throw new InvalidOperationException("Game changed; probe names are stale");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var readings = new List<object>();
        var watch = Stopwatch.StartNew();
        string? previous = null;
        while (watch.ElapsedMilliseconds < seconds * 1_000L)
        {
            runtime.FlushCachedData();
            var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
            var domain = runtime.AppDomains.Single();
            var root = module.GetTypeByName("oRGErqTKUmEPMiRFsMdt.ILqvIRTizh9luQa4ZLwE")!;
            var global = module.GetTypeByName("IsjDw5pRqGrqMy0QsTjL.a8qup8pRkLSjt8n8JXZm")!;
            var index = global.GetStaticFieldByName("ym6pBQ1rIAI")!.Read<int>(domain);
            var array = root.GetStaticFieldByName("ApvTK57RlqR")!.ReadObject(domain).AsArray();
            if (index <= 0 || index >= array.Length) throw new InvalidOperationException("Invalid local index");
            var actor = array.GetObjectValue(index);
            if (actor.IsNull || actor.ReadStringField("VJ0Sp4nQmlR", 64) != character)
                throw new InvalidOperationException("Local actor identity mismatch");
            int A(string field) => actor.ReadField<int>(field);
            int G(string field) => global.GetStaticFieldByName(field)!.Read<int>(domain);
            var spell = G("UrgpBisApFG");
            var start = G("aE5pBzxaiN6");
            var actorSpell = A("JOxSSLHxcEy");
            var candidates = new[] { "lsqSrei7qMm", "U6ZSrboTby4", "aE4SrPPhvAy", "IO5SrBBK0Jh" }
                .ToDictionary(field => field, A);
            var fingerprint = JsonSerializer.Serialize(new { spell, start, actorSpell, candidates });
            if (fingerprint != previous)
            {
                readings.Add(new { AtMs = watch.ElapsedMilliseconds, GlobalSpell = spell, GlobalStart = start,
                    PlayerSpell = actorSpell, GameClock = global.GetStaticFieldByName("vpXpCkD3Y8b")!.Read<long>(domain),
                    CandidateEnds = candidates });
                previous = fingerprint;
            }
            Thread.Sleep(80);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Readings = readings,
            Warning = "Observed values only; cast semantics and actuation not approved." }));
        return 0;
    }
}
