using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using NinOnlineAutoAssist.Diagnostics;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Core.Abstractions;

// Measures scheduling only. No game attachment and no keyboard input is sent.
if (args.Length == 1 && args[0] == "--response-timing") return ResponseTimingProbe.Run();
if (args.Length == 2 && args[0] == "--replay-cast-turn") return CastTraceReplay.Run(args[1]);
if (args.Length == 4 && args[0] == "--cast-state-trace" && int.TryParse(args[1], out var castPid) && int.TryParse(args[3], out var castSeconds))
    return CastStateTrace.Run(castPid, args[2], castSeconds);
if (args.Length == 3 && args[0] == "--village-probe" && int.TryParse(args[1], out var villagePid))
    return VillageProbe.Run(villagePid, args[2]);
if (args.Length == 3 && args[0] == "--projectile-catalog" && int.TryParse(args[1], out var catalogPid))
    return ProjectileCatalogProbe.Run(catalogPid, args[2]);
if (args.Length == 3 && args[0] == "--watch-local-projectiles" && int.TryParse(args[1], out var projectilePid))
    return ProjectileProbe.Run(projectilePid, args[2]);
if (args.Length == 2 && args[0] == "--replay-sub-anchors") return SubAnchorReplay.Run(args[1]);
if (args.Length == 4 && args[0] == "--sub-anchor-trace" && int.TryParse(args[1], out var anchorPid) && int.TryParse(args[3], out var anchorSeconds))
    return SubAnchorTrace.Run(anchorPid, args[2], anchorSeconds);
if (args.Length == 2 && args[0] == "--build-schema" && int.TryParse(args[1], out var schemaPid)) return BuildSchemaProbe.Run(schemaPid);
if (args.Length == 2 && args[0] == "--field-metadata") return FieldMetadata.Run(args[1]);
if (args.Length == 2 && args[0] == "--common-enums") return FieldMetadata.Run(args[1], enums: true);
if (args.Length == 1 && args[0] == "--hold-timing")
{
    var clock = new SystemMonotonicClock();
    for (var i = 0; i < 5; i++)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        for (var n = 0; n < 8; n++) Thread.Sleep(5);
        var countedSleepsMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        BoundedInputHold.Wait(40, clock, () => true);
        Console.WriteLine(JsonSerializer.Serialize(new { countedSleepsMs, deadlineHoldMs = watch.Elapsed.TotalMilliseconds }));
    }
    return 0;
}

if (args.Length == 3 && args[0] == "--live-snapshot" && int.TryParse(args[1], out var livePid))
{
    using var provider = new ReadOnlyGameStateProvider(livePid, args[2], new SystemMonotonicClock());
    for (var i = 0; i < 5; i++)
    {
        provider.GetSnapshot();
        Console.WriteLine(JsonSerializer.Serialize(new { provider.Status, provider.Frame }));
        Thread.Sleep(120);
    }
    return provider.IsLiveData ? 0 : 1;
}

if (args.Length == 3 && args[0] == "--watch-buffs" && int.TryParse(args[1], out var buffPid))
{
    using var provider = new ReadOnlyGameStateProvider(buffPid, args[2], new SystemMonotonicClock());
    var watch = System.Diagnostics.Stopwatch.StartNew();
    string? previous = null;
    while (watch.Elapsed < TimeSpan.FromSeconds(90))
    {
        provider.GetSnapshot();
        var signature = provider.Frame is { } frame
            ? JsonSerializer.Serialize(frame.ObservedActiveBuffs) : provider.Status;
        if (signature != previous)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { AtMs = watch.ElapsedMilliseconds, provider.Status,
                Buffs = provider.Frame?.ObservedActiveBuffs }));
            previous = signature;
        }
        Thread.Sleep(50);
    }
    return 0;
}

if (args.Length == 3 && args[0] == "--navigation-snapshot" && int.TryParse(args[1], out var navigationPid))
{
    using var provider = new ReadOnlyGameStateProvider(navigationPid, args[2], new SystemMonotonicClock()) { ReadNavigation = true };
    for (var i = 0; i < 3; i++)
    {
        provider.GetSnapshot();
        Console.WriteLine(JsonSerializer.Serialize(new { provider.Status, provider.NavigationStatus,
            ReadMs = provider.Frame?.ReadDurationMs, Player = provider.Frame?.Snapshot.Player,
            Navigation = provider.Navigation is { } n ? new { n.Map, n.OriginX, n.OriginY, n.Width, n.Height,
                n.DynamicHazardsKnown, n.VisibleHazards, Cells = n.Cells.GroupBy(c => c).Select(g => new { Type = g.Key.ToString(), Count = g.Count() }) } : null }));
        Thread.Sleep(150);
    }
    return provider.Navigation is not null ? 0 : 1;
}

if (args.Length == 3 && args[0] == "--map-players-snapshot" && int.TryParse(args[1], out var mapPlayersPid))
{
    using var provider = new ReadOnlyGameStateProvider(mapPlayersPid, args[2], new SystemMonotonicClock()) { ReadMapPlayers = true };
    for (var i = 0; i < 3; i++)
    {
        provider.GetSnapshot();
        Console.WriteLine(JsonSerializer.Serialize(new { provider.Status, provider.MapPlayersStatus,
            provider.MapPlayers, provider.Frame?.Stamina }));
        Thread.Sleep(200);
    }
    return provider.MapPlayers is not null ? 0 : 1;
}

if (args.Length == 2 && args[0] == "--process" && int.TryParse(args[1], out var pid))
    return LiveInspection.Run(pid);
if (args.Length == 3 && args[0] == "--terrain-probe" && int.TryParse(args[1], out var terrainPid))
    return TerrainProbe.Run(terrainPid, args[2]);
if (args.Length == 3 && args[0] == "--stamina-probe" && int.TryParse(args[1], out var staminaPid))
    return StaminaProbe.Run(staminaPid, args[2]);
if (args.Length == 2 && args[0] == "--state-probe" && int.TryParse(args[1], out var probePid))
    return StateProbe.Run(probePid);
if (args.Length == 2 && args[0] == "--watch-state-probe" && int.TryParse(args[1], out var watchPid))
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(35))
    {
        if (StateProbe.Run(watchPid, compact: true) != 0) return 1;
        Thread.Sleep(150);
    }
    return 0;
}

// Metadata inspection only: never load or execute the target assembly.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: NinOnlineAutoAssist.Diagnostics <assembly.dll> | --process <NinOnline PID>");
    Console.Error.WriteLine("Read-only live projection: --live-snapshot <PID> <character name>");
    Console.Error.WriteLine("Calibration only: --state-probe <PID> | --watch-state-probe <PID> (35 seconds)");
    return 2;
}
using var stream = File.OpenRead(args[0]);
var hash = Convert.ToHexString(SHA256.HashData(stream));
stream.Position = 0;
using var pe = new PEReader(stream);
if (!pe.HasMetadata)
{
    Console.Error.WriteLine("No managed metadata.");
    return 1;
}
var reader = pe.GetMetadataReader();
var types = reader.TypeDefinitions.Select(h =>
{
    var t = reader.GetTypeDefinition(h);
    return new
    {
        Name = reader.GetString(t.Namespace) + "." + reader.GetString(t.Name),
        Fields = t.GetFields().Select(f => reader.GetString(reader.GetFieldDefinition(f).Name)).ToArray(),
        Properties = t.GetProperties().Select(p => reader.GetString(reader.GetPropertyDefinition(p).Name)).ToArray()
    };
}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new
{
    File = Path.GetFileName(args[0]), Sha256 = hash,
    Mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid), Types = types
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;
