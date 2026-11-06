using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive phase comparison. Values are candidates, never a validated game profile.</summary>
public static class PlayerMovementProbe
{
    public static int Run(int pid, string character, bool shortCheck = false)
    {
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || character.Length is < 1 or > 64)
            throw new InvalidOperationException("Expected NinOnline and an explicit character name");
        var gamePath = Path.Combine(Path.GetDirectoryName(process.MainModule!.FileName)!, "NinOnline.dll");
        using (var gameFile = File.OpenRead(gamePath))
            if (Convert.ToHexString(SHA256.HashData(gameFile)) != "B384E2E1EE84A5AC0CB86AAE4F9A4A0A9D41C4F3E68195F9AC434656C9B05A60")
                throw new InvalidOperationException("Game DLL changed; refusing stale probe fields");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var phases = new List<object>();
        var watch = Stopwatch.StartNew();
        var previousPhase = -1;
        var samples = new List<(long Ms, int Phase, Dictionary<string, double> Values)>();
        while (watch.ElapsedMilliseconds < (shortCheck ? 3_000 : 50_000))
        {
            var ms = watch.ElapsedMilliseconds;
            var phase = shortCheck || ms < 10_000 ? 0 : ms < 40_000 ? 1 : 2;
            if (phase != previousPhase)
            {
                previousPhase = phase;
                Console.Error.WriteLine(phase switch
                {
                    0 => "PARADO: aguarde 10 segundos.",
                    1 => "ANDE agora por 30 segundos (preferencialmente em uma direcao).",
                    _ => "PARE agora; aguarde 10 segundos."
                });
            }
            // ClrMD caches heap reads. Reacquire wrappers on every sample or movement appears stationary.
            runtime.FlushCachedData();
            var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
            var domain = runtime.AppDomains.Single();
            var root = module.GetTypeByName("oRGErqTKUmEPMiRFsMdt.ILqvIRTizh9luQa4ZLwE")
                ?? throw new InvalidOperationException("Build root changed");
            var players = root.GetStaticFieldByName("ApvTK57RlqR")
                ?? throw new InvalidOperationException("Player array changed");
            var array = players.ReadObject(domain);
            if (array.IsArray && array.AsArray().Length == 501)
            {
                var entries = array.AsArray();
                for (var i = 1; i < entries.Length; i++)
                {
                    var actor = entries.GetObjectValue(i);
                    if (actor.IsNull || actor.Type?.GetFieldByName("VJ0Sp4nQmlR")?.Type?.Name != "System.String" ||
                        !string.Equals(actor.ReadStringField("VJ0Sp4nQmlR", 64), character, StringComparison.Ordinal)) continue;
                    var values = new Dictionary<string, double>();
                    foreach (var field in actor.Type.Fields)
                    {
                        try
                        {
                            double? value = field.Type?.Name switch
                            {
                                "System.Int32" => field.Read<int>(actor.Address, false),
                                "System.Int64" => field.Read<long>(actor.Address, false),
                                "System.Byte" => field.Read<byte>(actor.Address, false),
                                "System.Single" => field.Read<float>(actor.Address, false),
                                _ => null
                            };
                            if (value is { } number && double.IsFinite(number)) values[field.Name!] = number;
                        }
                        catch (InvalidOperationException) { }
                    }
                    samples.Add((ms, phase, values));
                    break;
                }
            }
            Thread.Sleep(120);
        }
        // The full trace remains local; no player names, tokens or other actors are emitted.
        foreach (var phase in Enumerable.Range(0, shortCheck ? 1 : 3))
        {
            var subset = samples.Where(s => s.Phase == phase).ToArray();
            if (subset.Length == 0) { phases.Add(new { Phase = phase, Samples = 0, Changes = Array.Empty<object>() }); continue; }
            phases.Add(new { Phase = phase, Samples = subset.Length, FirstMs = subset.FirstOrDefault().Ms,
                LastMs = subset.LastOrDefault().Ms,
                Changes = subset.SelectMany(s => s.Values.Keys).Distinct().Select(name => new
                {
                    Field = name,
                    First = subset[0].Values.GetValueOrDefault(name),
                    Last = subset[^1].Values.GetValueOrDefault(name),
                    Min = subset.Min(s => s.Values.GetValueOrDefault(name)),
                    Max = subset.Max(s => s.Values.GetValueOrDefault(name))
                }).Where(c => c.Min != c.Max).ToArray() });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Phases = phases,
            Warning = "Read-only candidates only. Phase timing depends on the user; no profile or input approved." }));
        return samples.Count < 10 ? 1 : 0;
    }
}
