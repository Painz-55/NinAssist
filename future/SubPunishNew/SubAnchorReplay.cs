using System.Text.Json;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics; // Archived: not compiled in the shipping diagnostic tool.

/// <summary>Offline event replay with a recording executor: never sends keyboard input.</summary>
public static class SubAnchorReplay
{
    private sealed class Clock : IMonotonicClock { public long NowMs { get; set; } }
    private sealed class Executor(Clock clock) : IActionExecutor
    {
        public List<object> Events { get; } = new();
        public int Starts { get; private set; }
        public ActionResult Execute(string actionId, string key)
        {
            if (actionId == "basic_attack") Starts++;
            Events.Add(new { AtMs = clock.NowMs, actionId, key }); return ActionResult.Ok("REPLAY ONLY");
        }
    }
    public static int Run(string path)
    {
        var clock = new Clock(); var executor = new Executor(clock);
        var engine = new AnchoredSubPunishEngine(executor, clock, new EarlyPunishOptions { FirstToSecondMs = 10, SecondToThirdMs = 100 });
        var samples = 0;
        foreach (var line in File.ReadLines(path))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Kind", out var kind) || kind.GetString() != "Sample") continue;
            var at = root.GetProperty("CapturedAtMs").GetInt64(); clock.NowMs = at;
            var player = root.GetProperty("Player").Deserialize<PlayerState>()!;
            var buffs = root.GetProperty("Buffs").Deserialize<ObservedBuff[]>()!;
            var known = buffs.Where(b => SubstitutionBuffs.IsKnown(b.Name)).ToArray();
            var frame = new LiveFrame(new GameSnapshot { Player = player }, at, root.GetProperty("Map").GetInt32(),
                "Replay local", null, known.Length > 0, known.FirstOrDefault()?.RemainingSeconds, 0) { ObservedActiveBuffs = buffs };
            engine.Update(frame, true, true, true); samples++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Mode = "OFFLINE ONLY / zero native inputs", Samples = samples,
            ComboStarts = executor.Starts, Events = executor.Events, Note = "Replays position/buff evidence; not native input latency or server acceptance" }));
        return 0;
    }
}
