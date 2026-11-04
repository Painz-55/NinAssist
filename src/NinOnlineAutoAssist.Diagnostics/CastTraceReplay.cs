using System.Text.Json;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Offline replay with a synthetic stable target. NO process attachment and NO keyboard input.</summary>
public static class CastTraceReplay
{
    private sealed class Clock : IMonotonicClock { public long NowMs { get; set; } }
    public static int Run(string path)
    {
        var clock = new Clock();
        var states = new Dictionary<string, JsonElement>();
        var turns = new List<object>();
        CastMemoryFields? fields = null;
        var engine = new AutoCastTurnEngine(clock, new(), direction =>
        {
            turns.Add(new { AtMs = clock.NowMs, Direction = direction.ToString() });
            return TurnResult.Ok("OFFLINE synthetic input only");
        });
        var casts = new HashSet<(int Spell, int Start)>();
        var samples = 0; var unknown = 0;
        foreach (var line in File.ReadLines(path))
        {
            using var json = JsonDocument.Parse(line);
            var row = json.RootElement;
            var kind = row.GetProperty("Kind").GetString();
            if (kind == "Header")
            {
                var profile = GameMemoryProfile.Select(row.GetProperty("Sha256").GetString()!, row.GetProperty("CommonSha256").GetString()!);
                fields = profile.CastTiming ?? throw new InvalidOperationException("No calibrated cast profile");
            }
            if (kind == "Discarded") { engine.Reset(); continue; }
            if (kind != "Sample" || fields is null) continue;
            foreach (var pair in row.GetProperty("Changes").EnumerateObject()) states[pair.Name] = pair.Value.Clone();
            long Number(string key) => states[key].GetInt64();
            var spell = (int)Number("Globals." + fields.GlobalSpell);
            var start = (int)Number("Globals." + fields.GlobalStart);
            var cast = CastObservation.Decode(spell, start, (int)Number("Player." + fields.PlayerSpell),
                (int)Number("Player." + fields.PlayerEnd), Number("Globals." + fields.GameClock));
            if (spell > 0) casts.Add((spell, start));
            if (cast is null) unknown++;
            var at = row.GetProperty("AtMs").GetInt64();
            var read = row.GetProperty("ReadMs").GetInt64();
            clock.NowMs = at + read;
            if (cast is not null) cast = cast with { ObservedAtMs = clock.NowMs };
            var frame = new LiveFrame(new GameSnapshot
            {
                Player = new PlayerState { Id = "synthetic-local", Facing = Direction.Down },
                Target = new TargetInfo { Id = "synthetic-target", IsTargeted = true, PositionY = -5 }
            }, at, row.GetProperty("Map").GetInt32(), "Synthetic", "Synthetic", false, null, read) { Cast = cast };
            engine.Update(frame, true, false); samples++;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Warning = "OFFLINE synthetic target; not evidence of game/server input acceptance. Observation instant approximated by end of diagnostic read.",
            Samples = samples, CastsObserved = casts.Count, UnknownSamples = unknown, PredictedTurns = turns.Count, Turns = turns }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
