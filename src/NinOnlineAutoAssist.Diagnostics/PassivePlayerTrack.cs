using System.Security.Cryptography;
using System.Text.Json;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Short read-only trace to establish whether hidden actors keep a live position.
/// Coordinates are client records, not proof of visibility or that a decoy is a player.</summary>
public static class PassivePlayerTrack
{
    public static int Run(int pid, string character, string signedManifest, int seconds)
    {
        if (seconds is < 5 or > 120) throw new ArgumentOutOfRangeException(nameof(seconds));
        using var process = System.Diagnostics.Process.GetProcessById(pid);
        var directory = Path.GetDirectoryName(process.MainModule?.FileName)
            ?? throw new InvalidOperationException("Game executable path unavailable");
        if (process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
        static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
        var bytes = File.ReadAllBytes(signedManifest);
        var signature = Convert.FromBase64String(File.ReadAllText(signedManifest + ".sig").Trim());
        var profile = SignedProfileCatalog.VerifyAndSelect(bytes, signature, SignedProfileCatalog.PublicKey,
            Hash(Path.Combine(directory, "NinOnline.dll")), Hash(Path.Combine(directory, "Common.dll")))
            ?? throw new InvalidOperationException("No signed profile for the exact game DLL pair");
        // A diagnostic never imports the actuation authorization from a signed app profile.
        profile = profile with { ActuationValidated = false, ValidatedInputKinds = [], ValidatedAttackModes = [], PunishComboValidated = false };
        var clock = new SystemMonotonicClock();
        using var provider = new ReadOnlyGameStateProvider(pid, character, clock, profile) { ReadMapPlayers = true };
        var start = clock.NowMs;
        var samples = new List<object>();
        int reads = 0, frames = 0;
        while (clock.NowMs - start < seconds * 1000L)
        {
            reads++;
            provider.GetSnapshot();
            var frame = provider.Frame;
            var map = provider.MapPlayers;
            if (frame is not null && map?.IsCurrent(frame, clock.NowMs) == true)
            {
                frames++;
                var me = frame.Snapshot.Player;
                samples.Add(new { AtMs = clock.NowMs - start, frame.Map, Local = new { me.Id, me.PositionX, me.PositionY },
                    Selected = frame.Snapshot.Target?.Id, map.GridWidth, map.GridHeight,
                    Players = map.Players.Where(p => p.X.HasValue && p.Y.HasValue && !p.IsLocal)
                        .Select(p => new { p.Slot, p.Name, p.X, p.Y, p.IsSelected }).ToArray(),
                    map.UnreadableSlots });
            }
            Thread.Sleep(100);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { Reads = reads, Frames = frames, provider.Status, Samples = samples,
            Warning = "Client-side positions only. A static position can be stale or a decoy. No input or overlay dispatch." }));
        return frames > 0 ? 0 : 1;
    }
}
