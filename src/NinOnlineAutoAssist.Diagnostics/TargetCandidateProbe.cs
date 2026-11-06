using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only target transition evidence; no target is inferred from a nearby player.</summary>
public static class TargetCandidateProbe
{
    public static int Run(int pid, string character)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || character.Length is < 1 or > 64)
            throw new InvalidOperationException("Expected NinOnline and explicit character");
        using (var file = File.OpenRead(Path.Combine(Path.GetDirectoryName(process.MainModule!.FileName)!, "NinOnline.dll")))
            if (Convert.ToHexString(SHA256.HashData(file)) != "B384E2E1EE84A5AC0CB86AAE4F9A4A0A9D41C4F3E68195F9AC434656C9B05A60")
                throw new InvalidOperationException("Build changed; refusing stale target probe");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var observations = new List<object>();
        string? previous = null;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 25_000)
        {
            runtime.FlushCachedData();
            var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
            var domain = runtime.AppDomains.Single();
            var roots = module.GetTypeByName("oRGErqTKUmEPMiRFsMdt.ILqvIRTizh9luQa4ZLwE")!;
            var globals = module.GetTypeByName("IsjDw5pRqGrqMy0QsTjL.a8qup8pRkLSjt8n8JXZm")!;
            var local = globals.GetStaticFieldByName("ym6pBQ1rIAI")!.Read<int>(domain);
            var array = roots.GetStaticFieldByName("ApvTK57RlqR")!.ReadObject(domain).AsArray();
            if (local <= 0 || local >= array.Length || array.GetObjectValue(local).ReadStringField("VJ0Sp4nQmlR", 64) != character)
                throw new InvalidOperationException("Local identity changed");
            var selected = globals.GetStaticFieldByName("t0vpBYkcQwH")!.ReadObject(domain);
            var fields = selected.IsNull ? null : selected.Type!.Fields.Where(f => f.Type?.Name == "System.Int32")
                .ToDictionary(f => f.Name!, f => f.Read<int>(selected.Address, false));
            var marker = fields is null ? "null" : JsonSerializer.Serialize(fields);
            if (marker != previous)
            {
                var candidateIndex = fields?.GetValueOrDefault("I5eSFtIUjUq") ?? 0;
                var linked = candidateIndex > 0 && candidateIndex < array.Length && candidateIndex != local
                    ? array.GetObjectValue(candidateIndex) : default;
                observations.Add(new { AtMs = watch.ElapsedMilliseconds, SelectedIsNull = selected.IsNull,
                    Fields = fields,
                    OtherPlayerRecordAtCandidateIndex = !linked.IsNull &&
                        !string.IsNullOrWhiteSpace(linked.ReadStringField("VJ0Sp4nQmlR", 64)) });
                previous = marker;
            }
            Thread.Sleep(100);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Observations = observations,
            Warning = "Candidate values only; a player target needs independent confirmation. No input approved." }));
        return 0;
    }
}
