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
if (args.Length == 2 && args[0] == "--build-schema" && int.TryParse(args[1], out var schemaPid)) return BuildSchemaProbe.Run(schemaPid);
if (args.Length == 2 && args[0] == "--profile-candidates" && int.TryParse(args[1], out var candidatePid))
    return ProfileCandidateProbe.Run(candidatePid);
if (args.Length == 4 && args[0] == "--identify-player-candidates" && int.TryParse(args[1], out var identityPid))
    return ProfileCandidateProbe.Identify(identityPid, args[2], args[3]);
if (args.Length == 11 && args[0] == "--candidate-structures" && int.TryParse(args[1], out var structurePid))
    return CandidateStructureProbe.Run(structurePid, args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10]);
if (args.Length == 3 && args[0] == "--trace-player-movement" && int.TryParse(args[1], out var movementPid))
    return PlayerMovementProbe.Run(movementPid, args[2]);
if (args.Length == 3 && args[0] == "--check-player-freshness" && int.TryParse(args[1], out var freshnessPid))
    return PlayerMovementProbe.Run(freshnessPid, args[2], shortCheck: true);
if (args.Length is 3 or 4 && args[0] == "--trace-cast-candidates" && int.TryParse(args[1], out var castCandidatePid) &&
    (args.Length == 3 || int.TryParse(args[3], out _)))
    return CastCandidateProbe.Run(castCandidatePid, args[2], args.Length == 4 ? int.Parse(args[3]) : 30);
if (args.Length == 3 && args[0] == "--profile-details" && int.TryParse(args[1], out var detailsPid))
    return ProfileDetailsProbe.Run(detailsPid, args[2]);
if (args.Length == 3 && args[0] == "--watch-target-candidates" && int.TryParse(args[1], out var targetPid))
    return TargetCandidateProbe.Run(targetPid, args[2]);
if (args.Length == 4 && args[0] == "--calibration-snapshot" && int.TryParse(args[1], out var calibrationPid))
{
    var profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(args[3]))
        ?? throw new InvalidOperationException("Missing calibration profile");
    if (profile.ActuationValidated || profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0)
        throw new InvalidOperationException("Calibration must explicitly disable all input");
    using var provider = new ReadOnlyGameStateProvider(calibrationPid, args[2], new SystemMonotonicClock(),
        profile with { ExternalProfile = true }) { ReadNavigation = true, ReadMapPlayers = true };
    for (var i = 0; i < 12; i++) { provider.GetSnapshot(); Thread.Sleep(100); }
    Console.WriteLine(JsonSerializer.Serialize(new { provider.Status, provider.Frame, provider.NavigationStatus,
        Navigation = provider.Navigation is { } n ? new { n.Map, n.Width, n.Height, n.DynamicHazardsKnown } : null,
        provider.MapPlayersStatus, MapPlayers = provider.MapPlayers?.Players.Count }));
    return provider.Frame is null ? 1 : 0;
}
if (args.Length == 5 && args[0] == "--calibration-watch" && int.TryParse(args[1], out var watchCalibrationPid) &&
    int.TryParse(args[4], out var watchCalibrationSeconds))
    return CalibrationWatch.Run(watchCalibrationPid, args[2], args[3], watchCalibrationSeconds);
if (args.Length == 4 && args[0] == "--signed-profile-snapshot" && int.TryParse(args[1], out var signedPid))
{
    using var process = System.Diagnostics.Process.GetProcessById(signedPid);
    var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
    static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    var json = File.ReadAllBytes(args[3]);
    var sig = Convert.FromBase64String(File.ReadAllText(args[3] + ".sig").Trim());
    var profile = SignedProfileCatalog.VerifyAndSelect(json, sig, SignedProfileCatalog.PublicKey,
        Hash(Path.Combine(directory, "NinOnline.dll")), Hash(Path.Combine(directory, "Common.dll")))
        ?? throw new InvalidOperationException("No signed profile for this exact game build");
    // A diagnostic never dispatches input, even when the signed app profile allows a subset of actions.
    profile = profile with { ActuationValidated = false, ValidatedInputKinds = [] };
    using var provider = new ReadOnlyGameStateProvider(signedPid, args[2] == "--auto" ? "" : args[2],
        new SystemMonotonicClock(), profile) { ReadNavigation = true, ReadMapPlayers = true };
    for (var i = 0; i < 6; i++) { provider.GetSnapshot(); Thread.Sleep(100); }
    Console.WriteLine(JsonSerializer.Serialize(new { provider.Status, provider.Frame, provider.NavigationStatus,
        Navigation = provider.Navigation is null ? null : new { provider.Navigation.Map, provider.Navigation.Width, provider.Navigation.Height },
        provider.MapPlayersStatus, MapPlayers = provider.MapPlayers?.Players.Count }));
    return provider.Frame is null ? 1 : 0;
}
if (args.Length == 2 && args[0] == "--field-metadata") return FieldMetadata.Run(args[1]);
if (args.Length == 5 && args[0] == "--profile-schema-diff")
    return ProfileSchemaDiff.Run(args[1], args[2], args[3], args[4]);
if (args.Length == 3 && args[0] == "--capture-profile-baseline")
    return ProfileSchemaDiff.Capture(args[1], args[2]);
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
    using var provider = new ReadOnlyGameStateProvider(livePid, args[2] == "--auto" ? "" : args[2], new SystemMonotonicClock());
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
    using var provider = new ReadOnlyGameStateProvider(navigationPid, args[2] == "--auto" ? "" : args[2], new SystemMonotonicClock()) { ReadNavigation = true };
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
    using var provider = new ReadOnlyGameStateProvider(mapPlayersPid, args[2] == "--auto" ? "" : args[2], new SystemMonotonicClock()) { ReadMapPlayers = true };
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
    Console.Error.WriteLine("Read-only live projection: --live-snapshot <PID> <character name|--auto>");
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
