using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;

namespace NinOnlineAutoAssist.Diagnostics;

public sealed record ObservedSpell(int UniqueIndex, byte CurrentLevel, string Name, int MPCost,
    float CastTime, int CDTime, int Range);
public sealed record ObservedType(string Name, int Count, string[] Fields);
public sealed record MemoryEvidence(string ObjectAddress, string MPCostAddress, string CDTimeAddress,
    string Warning = "Ephemeral addresses: may become invalid after GC. Never persist as a pointer profile.");
public sealed record ResourceInspection(int ProcessId, DateTimeOffset ObservedAtUtc, string Origin,
    string Consistency, int ObjectsVisited, long ElapsedMs, bool Truncated, int FailedSpellReads,
    IReadOnlyList<ObservedType> Types, IReadOnlyList<ObservedSpell> Spells, MemoryEvidence? SubstitutionSample);

public static class LiveInspection
{
    public static ResourceInspection Inspect(int pid, Func<bool>? mayContinue = null)
    {
            if (mayContinue?.Invoke() == false) throw new OperationCanceledException();
            using var process = Process.GetProcessById(pid);
            if (!process.ProcessName.Equals("NinOnline", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Expected NinOnline.exe; refusing another process.");
            // Passive OS reads: no suspension, debugger, injection, hooks or memory writes.
            using var target = DataTarget.AttachToProcess(pid, suspend: false);
            using var runtime = target.ClrVersions.Single().CreateRuntime();
            var counts = new Dictionary<string, (int Count, string[] Fields)>();
            var spells = new List<ObservedSpell>();
            var timer = Stopwatch.StartNew();
            var visited = 0;
            var truncated = false;
            var failed = 0;
            MemoryEvidence? evidence = null;
            foreach (var obj in runtime.Heap.EnumerateObjects())
            {
                if (mayContinue?.Invoke() == false) throw new OperationCanceledException();
                if (visited >= 2_000_000 || timer.Elapsed > TimeSpan.FromSeconds(20))
                {
                    truncated = true;
                    break;
                }
                visited++;
                var type = obj.Type;
                if (type is null || !string.Equals(Path.GetFileName(type.Module?.Name), "NinOnline.dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                var name = type.Name ?? "?";
                counts.TryGetValue(name, out var entry);
                counts[name] = (entry.Count + 1, entry.Fields ?? type.Fields.Select(f => $"{f.Name}:{f.Type?.Name}").ToArray());
                if (name == "client.SpellRec" && spells.Count < 5000)
                {
                    try
                    {
                        var expected = new Dictionary<string, string>
                        {
                            ["Name"] = "System.String", ["MPCost"] = "System.Int32",
                            ["CastTime"] = "System.Single", ["CDTime"] = "System.Int32",
                            ["Range"] = "System.Int32", ["UniqueIndex"] = "System.Int32", ["CurrentLevel"] = "System.Byte"
                        };
                        if (expected.Any(e => type.GetFieldByName(e.Key)?.Type?.Name != e.Value))
                            throw new InvalidOperationException("SpellRec schema changed; refusing to guess field types.");
                        var spellName = obj.ReadStringField("Name", 256);
                        if (!string.IsNullOrWhiteSpace(spellName))
                        {
                            spells.Add(new ObservedSpell(
                                obj.ReadField<int>("UniqueIndex"), obj.ReadField<byte>("CurrentLevel"), spellName,
                                obj.ReadField<int>("MPCost"), obj.ReadField<float>("CastTime"),
                                obj.ReadField<int>("CDTime"), obj.ReadField<int>("Range")));
                            if (spellName == "Substitution Technique" && evidence is null)
                                evidence = new MemoryEvidence($"0x{obj.Address:X}",
                                    $"0x{type.GetFieldByName("MPCost")!.GetAddress(obj.Address):X}",
                                    $"0x{type.GetFieldByName("CDTime")!.GetAddress(obj.Address):X}");
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { failed++; }
                }
            }
            return new ResourceInspection(pid, DateTimeOffset.UtcNow, "Observed",
                "Best-effort passive read; GC may change objects during traversal. Definitions, NOT equipped skills or remaining cooldowns.",
                visited, timer.ElapsedMilliseconds, truncated, failed,
                counts.OrderByDescending(p => p.Value.Count).Select(p => new ObservedType(p.Key, p.Value.Count, p.Value.Fields)).ToArray(),
                spells.Where(s => s.MPCost >= 0 && s.CDTime >= 0 && s.Range >= 0 && float.IsFinite(s.CastTime) && s.CastTime >= 0)
                    .Distinct().OrderBy(s => s.Name).ThenBy(s => s.CurrentLevel).ToArray(), evidence);
    }

    public static int Run(int pid)
    {
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(Inspect(pid), new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Read-only inspection unavailable: {ex.Message}");
            return 1;
        }
    }
}
