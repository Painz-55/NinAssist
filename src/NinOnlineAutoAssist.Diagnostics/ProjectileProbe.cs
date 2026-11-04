using System.Diagnostics;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Observe local projectile identities/geometry without sending keys.</summary>
public static class ProjectileProbe
{
    public static int Run(int pid, string character)
    {
        var clock = new SystemMonotonicClock();
        using var provider = new ReadOnlyGameStateProvider(pid, character, clock);
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        ulong rootsTable = 0;
        var watch = Stopwatch.StartNew();
        string? last = null;
        while (watch.Elapsed.TotalSeconds < 120)
        {
            try
            {
            provider.GetSnapshot();
            if (provider.Frame is not { } frame) { Thread.Sleep(25); continue; }
            var local = int.Parse(frame.Snapshot.Player.Id.Split(':')[0]);
            runtime.FlushCachedData();
            var roots = rootsTable == 0 ? null : runtime.GetTypeByMethodTable(rootsTable);
            if (roots is null)
            {
                roots = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll").GetTypeByName(provider.Profile.Roots)!;
                rootsTable = roots.MethodTable;
            }
            var domain = runtime.AppDomains.Single();
            var array = roots.GetStaticFieldByName(provider.Profile.Projectiles)!.ReadObject(domain).AsArray();
            var rows = new List<object>();
            for (var i = 0; i < Math.Min(array.Length, 8192); i++)
            {
                var p = array.GetObjectValue(i);
                if (p.IsNull || p.ReadField<int>("Owner") != local) continue;
                var values = new Dictionary<string, object?>();
                foreach (var field in new[] { "Owner", "OwnerType", "Spell", "Ammo", "Range", "Speed", "pic", "HitboxWidth", "HitboxLength", "StartupTime", "EndTime" })
                    values[field] = p.ReadField<int>(field);
                values["Generation"] = p.ReadField<uint>("Generation");
                foreach (var field in new[] { "X", "Y", "StartX", "StartY", "MoveProgress" }) values[field] = p.ReadField<double>(field);
                rows.Add(new { Slot = i, Values = values });
            }
            var signature = JsonSerializer.Serialize(rows);
            if (signature != last)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { AtMs = watch.ElapsedMilliseconds, Local = local, frame.PlayerName,
                    PlayerX = frame.Snapshot.Player.PositionX, PlayerY = frame.Snapshot.Player.PositionY, Projectiles = rows }));
                last = signature;
            }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            {
                // Projectile objects can disappear during a passive GC/read. Discard, flush, retry next sample.
                Console.WriteLine(JsonSerializer.Serialize(new { AtMs = watch.ElapsedMilliseconds, Discarded = true, Reason = ex.Message }));
                last = null;
            }
            Thread.Sleep(10);
        }
        return 0;
    }
}
