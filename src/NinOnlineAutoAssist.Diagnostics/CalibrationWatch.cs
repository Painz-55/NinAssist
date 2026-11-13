using System.Text.Json;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only provider transitions for an exact-hash calibration profile; no input dispatch.</summary>
public static class CalibrationWatch
{
    public static int Run(int pid, string character, string path, int seconds)
    {
        if (seconds is < 5 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        var profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Missing calibration profile");
        if (profile.ActuationValidated || profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0)
            throw new InvalidOperationException("Calibration must explicitly disable all input");
        var clock = new SystemMonotonicClock();
        using var provider = new ReadOnlyGameStateProvider(pid, character, clock, profile with { ExternalProfile = true })
        { ReadNavigation = true, ReadMapPlayers = true };
        var events = new List<object>();
        var started = clock.NowMs;
        var attempts = 0; var frames = 0; var navigation = 0; var mapPlayers = 0;
        string? previous = null;
        while (clock.NowMs - started < seconds * 1000L)
        {
            attempts++;
            provider.GetSnapshot();
            if (provider.Frame is { } frame)
            {
                frames++;
                if (provider.Navigation is not null) navigation++;
                if (provider.MapPlayers is not null) mapPlayers++;
                var stamp = new { frame.Map, frame.Snapshot.Player.Id,
                    frame.Snapshot.Player.PositionX, frame.Snapshot.Player.PositionY,
                    frame.Snapshot.Player.Facing, frame.Snapshot.Player.IsAlive,
                    Stamina = frame.Stamina?.Current,
                    Maximum = frame.Stamina?.Maximum, Running = frame.Stamina?.Running,
                    Target = frame.Snapshot.Target?.Id,
                    CastId = frame.Cast is { Active: true } cast ? cast.SpellId : 0,
                    frame.SubstitutionActive, frame.SubstitutionDuration,
                    NavigationMap = provider.Navigation?.Map,
                    NavigationKnown = provider.Navigation?.DynamicHazardsKnown,
                    NavigationStatus = provider.NavigationStatus,
                    MapPlayers = provider.MapPlayers?.Players.Count,
                    Buffs = frame.ObservedActiveBuffs.OrderBy(b => b.Name)
                        .Select(b => new { b.Name, b.RemainingSeconds }).ToArray() };
                var signature = JsonSerializer.Serialize(stamp);
                if (signature != previous)
                {
                    events.Add(new { AtMs = clock.NowMs - started, State = stamp });
                    previous = signature;
                }
            }
            Thread.Sleep(100);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Attempts = attempts, Frames = frames,
            NavigationFrames = navigation, MapPlayerFrames = mapPlayers, provider.Status, Events = events,
            Warning = "Read-only calibration only. Does not authorize input or publish a profile." }));
        return frames > 0 ? 0 : 1;
    }
}
