using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed record ObservedBuff(string Name, int? RemainingSeconds);

public sealed record LiveFrame(GameSnapshot Snapshot, long CapturedAtMs, int Map, string PlayerName,
    string? TargetName, bool SubstitutionActive, int? SubstitutionDuration, long ReadDurationMs)
{
    public StaminaObservation? Stamina { get; init; }
    public CastObservation? Cast { get; init; }
    public IReadOnlyList<ObservedBuff> ObservedActiveBuffs { get; init; } = Array.Empty<ObservedBuff>();
    // The legacy snapshot has non-nullable bools. These signals must not be mistaken for mapped observations.
    public IReadOnlyList<string> UnmappedSignals { get; init; } = new[]
        { "Casting", "ActionBlock", "SubstitutionCooldownRemaining", "Threat", "AttackerIdentity", "QSCostCooldownAndEffect" };
}

/// <summary>Targeted passive reads. No heap traversal, injected code, writes or fixed object addresses.</summary>
public sealed class ReadOnlyGameStateProvider : IGameStateProvider, IDisposable
{
    private readonly Process _process;
    private readonly DataTarget _target;
    private readonly ClrRuntime _runtime;
    private readonly IMonotonicClock _clock;
    private readonly LocalCharacterIdentity _identity;
    public string? DetectedCharacterName => _identity.Name;
    private ulong _globalsMethodTable, _rootsMethodTable;
    public int ProcessId => _process.Id;
    public string GameDirectory { get; }
    public GameMemoryProfile Profile { get; }
    public LiveFrame? Frame { get; private set; }
    public bool ReadNavigation { get; set; }
    public NavigationFrame? Navigation { get; private set; }
    public bool ReadMapPlayers { get; set; }
    public MapPlayersFrame? MapPlayers { get; private set; }
    public string MapPlayersStatus { get; private set; } = "Lista não solicitada";
    public string NavigationStatus { get; private set; } = "Navegação não solicitada";
    public string Status { get; private set; } = "Connecting";
    public bool IsLiveData => Frame is { } f && _clock.NowMs - f.CapturedAtMs <= 500;

    public ReadOnlyGameStateProvider(int pid, string expectedName, IMonotonicClock clock)
    {
        _identity = new LocalCharacterIdentity(expectedName);
        _clock = clock;
        _process = Process.GetProcessById(pid);
        try
        {
            if (_process.ProcessName != "NinOnline") throw new InvalidOperationException("Expected NinOnline.exe");
            var executable = _process.MainModule?.FileName ?? throw new InvalidOperationException("Process path unavailable");
            GameDirectory = Path.GetDirectoryName(executable)!;
            var assembly = Path.Combine(Path.GetDirectoryName(executable)!, "NinOnline.dll");
            string gameHash, commonHash;
            using (var file = File.OpenRead(assembly)) gameHash = Convert.ToHexString(SHA256.HashData(file));
            using (var file = File.OpenRead(Path.Combine(GameDirectory, "Common.dll"))) commonHash = Convert.ToHexString(SHA256.HashData(file));
            Profile = GameMemoryProfile.Select(gameHash, commonHash);
            _target = DataTarget.AttachToProcess(pid, suspend: false);
            try { _runtime = _target.ClrVersions.Single().CreateRuntime(); }
            catch { _target.Dispose(); throw; }
        }
        catch { _process.Dispose(); throw; }
    }

    public GameSnapshot? GetSnapshot()
    {
        Frame = null;
        Navigation = null;
        MapPlayers = null;
        var started = _clock.NowMs;
        try
        {
            if (_process.HasExited) throw new InvalidOperationException("Game exited; reconnect manually.");
            _runtime.FlushCachedData();
            // Cache only numeric type identifiers, NEVER ClrMD wrappers or GC object addresses across flushes.
            var globals = _globalsMethodTable == 0 ? null : _runtime.GetTypeByMethodTable(_globalsMethodTable);
            var roots = _rootsMethodTable == 0 ? null : _runtime.GetTypeByMethodTable(_rootsMethodTable);
            if (globals is null || roots is null)
            {
                var module = _runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
                globals = module.GetTypeByName(Profile.Globals) ?? throw new InvalidOperationException("Globals unavailable");
                roots = module.GetTypeByName(Profile.Roots) ?? throw new InvalidOperationException("Player roots unavailable");
                _globalsMethodTable = globals.MethodTable; _rootsMethodTable = roots.MethodTable;
            }
            if (globals.Name != Profile.Globals || roots.Name != Profile.Roots)
                throw new InvalidOperationException("Type identity changed; reconnect required");
            var domain = _runtime.AppDomains.Single();
            var local = StaticInt(globals, Profile.LocalIndex, domain);
            var selectedState = ReadSelectedTarget(globals, domain);
            var selected = selectedState.PlayerIndex;
            var arrayField = roots.GetStaticFieldByName(Profile.Players) ?? throw new InvalidOperationException("Player array unavailable");
            if (!arrayField.IsInitialized(domain) || arrayField.GetAddress(domain) == 0)
                throw new InvalidOperationException("Game state not initialized");
            var arrayObject = arrayField.ReadObject(domain);
            if (arrayObject.IsNull || !arrayObject.IsArray) throw new InvalidOperationException("Not in a map");
            var array = arrayObject.AsArray();
            if (local <= 0 || local >= array.Length || array.Length > 100_000) throw new InvalidOperationException("Invalid local index");
            var player = array.GetObjectValue(local);
            if (player.IsNull) throw new InvalidOperationException("Local player missing");
            var name = player.ReadStringField(Profile.Name, 64);
            if (_identity.IsInvalidated || !LocalCharacterIdentity.IsValidName(name))
                throw new InvalidOperationException("Local character changed/unavailable; confirm identity and reconnect.");
            if (_identity.Name is { } expected && name != expected)
            {
                // Latch a corroborated character change: even returning to the old character must not resume modules.
                if (StaticInt(globals, Profile.LocalIndex, domain) == local &&
                    arrayField.ReadObject(domain).Address == arrayObject.Address)
                    _identity.ConfirmStableObservation(name, player.ReadStringField(Profile.Name, 64));
                throw new InvalidOperationException("Local character changed; reconnect required.");
            }
            var map = Field<int>(player, Profile.Map);
            if (map <= 0) throw new InvalidOperationException("Player outside a valid map");
            var x = Coordinate(player, Profile.PixelX);
            var y = Coordinate(player, Profile.PixelY);
            var vitals = IntArray(player, Profile.Vitals, 4);
            var max = IntArray(player, Profile.MaxVitals, 4);
            CastObservation? cast = null;
            if (Profile.CastTiming is { } castFields)
            {
                try
                {
                    var spell = StaticInt(globals, castFields.GlobalSpell, domain);
                    var start = StaticInt(globals, castFields.GlobalStart, domain);
                    var actorSpell = Field<int>(player, castFields.PlayerSpell);
                    var end = Field<int>(player, castFields.PlayerEnd);
                    var clockField = globals.GetStaticFieldByName(castFields.GameClock);
                    if (clockField?.Type?.Name == "System.Int64" && clockField.IsInitialized(domain) &&
                        _target.DataReader.Read(clockField.GetAddress(domain), out long gameNow) && gameNow > 0 &&
                        spell == StaticInt(globals, castFields.GlobalSpell, domain) && start == StaticInt(globals, castFields.GlobalStart, domain) &&
                        actorSpell == Field<int>(player, castFields.PlayerSpell) && end == Field<int>(player, castFields.PlayerEnd))
                    {
                        var decoded = CastObservation.Decode(spell, start, actorSpell, end, gameNow);
                        if (decoded is not null) cast = decoded with { ObservedAtMs = _clock.NowMs };
                    }
                }
                catch { /* An unknown/inconsistent cast never becomes a completion trigger or an idle baseline. */ }
            }
            StaminaObservation? stamina = null;
            var runFlag = globals.GetStaticFieldByName(Profile.Running);
            if (vitals[3] >= 0 && max[3] > 0 && vitals[3] <= max[3] &&
                runFlag?.Type?.Name == "System.Boolean" && runFlag.IsInitialized(domain) &&
                _target.DataReader.Read(runFlag.GetAddress(domain), out byte running) && running <= 1)
                stamina = new(vitals[3], max[3], running == 1);
            if (vitals[1] < 0 || max[1] <= 0 || vitals[1] > max[1] || vitals[2] < 0 || vitals[2] > max[2])
                throw new InvalidOperationException("Inconsistent vitals");
            var buffs = new List<string>();
            var observedBuffs = new List<ObservedBuff>();
            var buffObject = player.ReadObjectField(Profile.Buffs);
            if (buffObject.IsNull || !buffObject.IsArray) throw new InvalidOperationException("Buff state unavailable");
            var buffArray = buffObject.AsArray();
            if (buffArray.Length > 256) throw new InvalidOperationException("Invalid buff array");
            for (var i = 0; i < buffArray.Length; i++)
            {
                var buff = buffArray.GetObjectValue(i);
                if (buff.IsNull || Field<int>(buff, Profile.BuffActive) != 1) continue;
                var buffName = buff.ReadStringField(Profile.BuffName, 128);
                if (string.IsNullOrWhiteSpace(buffName)) continue;
                buffs.Add(buffName);
                var duration = Field<int>(buff, Profile.BuffDuration);
                observedBuffs.Add(new ObservedBuff(buffName, duration));
            }
            TargetInfo? target = null;
            string? targetName = null;
            if (selected > 0 && selected < array.Length && selected != local)
            {
                var entity = array.GetObjectValue(selected);
                if (!entity.IsNull && Field<int>(entity, Profile.Map) == map)
                {
                    targetName = entity.ReadStringField(Profile.Name, 64);
                    if (!string.IsNullOrWhiteSpace(targetName))
                    {
                        var hp = IntArray(entity, Profile.Vitals, 4)[1];
                        target = new TargetInfo { Id = $"{selected}:{targetName}", PositionX = Coordinate(entity, Profile.PixelX),
                            PositionY = Coordinate(entity, Profile.PixelY), IsAlive = hp > 0, IsTargeted = true };
                    }
                }
            }
            if (StaticInt(globals, Profile.LocalIndex, domain) != local ||
                ReadSelectedTarget(globals, domain) != selectedState ||
                Field<int>(player, Profile.Map) != map || arrayField.ReadObject(domain).Address != arrayObject.Address ||
                !_identity.ConfirmStableObservation(name, player.ReadStringField(Profile.Name, 64)))
                throw new InvalidOperationException("State changed during read; retry next tick");
            var snapshot = new GameSnapshot
            {
                Player = new PlayerState { Id = $"{local}:{name}", PositionX = x, PositionY = y,
                    Facing = DecodeFacing(Field<byte>(player, Profile.Facing)), IsAlive = vitals[1] > 0,
                    Resources = vitals[2] },
                Target = target, KnownEntities = target is null ? Array.Empty<TargetInfo>() : new[] { target },
                ActiveBuffs = buffs
            };
            if (ReadNavigation)
            {
                try
                {
                    Navigation = ReadTerrain(roots, domain, local, map, x, y, started);
                    NavigationStatus = $"Terreno observado + {Navigation.VisibleHazards} perigos; {Navigation.IgnoredOwnShots} disparos próprios reconhecidos ignorados";
                }
                catch (Exception ex) { Navigation = null; NavigationStatus = $"Navegação bloqueada: {ex.Message}"; }
            }
            else NavigationStatus = "Navegação não solicitada (Follow OFF/tecla solta)";
            if (ReadMapPlayers)
            {
                try
                {
                    var players = new List<MapPlayer>();
                    var unreadable = 0;
                    for (var i = 1; i < array.Length; i++)
                    {
                        try
                        {
                            var entity = array.GetObjectValue(i);
                            if (entity.IsNull || Field<int>(entity, Profile.Map) != map) continue;
                            var playerName = entity.ReadStringField(Profile.Name, 64);
                            if (string.IsNullOrWhiteSpace(playerName)) continue;
                            double? ex = null, ey = null;
                            try { ex = Coordinate(entity, Profile.PixelX); ey = Coordinate(entity, Profile.PixelY); }
                            catch { /* Keep named map records even when position is not available. */ }
                            var village = PlayerVillage.Unknown;
                            if (Profile.Village is { } villageField)
                            {
                                try
                                {
                                    var code = Field<byte>(entity, villageField);
                                    if (code == Field<byte>(entity, villageField)) village = PlayerVillages.FromCode(code);
                                }
                                catch { /* Missing/unreadable village must not remove the named map record. */ }
                            }
                            if (array.GetObjectValue(i).Address != entity.Address ||
                                Field<int>(entity, Profile.Map) != map || entity.ReadStringField(Profile.Name, 64) != playerName)
                            { unreadable++; continue; }
                            // Deliberately no render/visibility/sprite filter: include transformed/hidden records if received.
                            players.Add(new(i, playerName, map, ex, ey, i == local, i == selected) { Village = village });
                        }
                        catch { unreadable++; }
                    }
                    if (arrayField.ReadObject(domain).Address != arrayObject.Address ||
                        StaticInt(globals, Profile.LocalIndex, domain) != local || Field<int>(player, Profile.Map) != map)
                        throw new InvalidOperationException("Mapa/lista mudou durante leitura");
                    MapPlayers = new(map, $"{local}:{name}", started, MapPlayersFrame.Filter(players, map), unreadable);
                    MapPlayersStatus = $"{MapPlayers.Players.Count} registros locais no mapa {map}";
                }
                catch (Exception ex) { MapPlayers = null; MapPlayersStatus = $"Lista indisponível: {ex.Message}"; }
            }
            if (StaticInt(globals, Profile.LocalIndex, domain) != local || Field<int>(player, Profile.Map) != map ||
                player.ReadStringField(Profile.Name, 64) != name || arrayField.ReadObject(domain).Address != arrayObject.Address)
                throw new InvalidOperationException("Local identity/map changed during read");
            var elapsed = _clock.NowMs - started;
            if (elapsed > 500) throw new InvalidOperationException($"Read too slow ({elapsed} ms); no inputs allowed");
            // Age includes time spent reading; publishing a slow read must not make old data look new.
            Frame = new LiveFrame(snapshot, started, map, name!, targetName,
                observedBuffs.Any(b => SubstitutionBuffs.IsKnown(b.Name)),
                observedBuffs.Where(b => SubstitutionBuffs.IsKnown(b.Name)).Select(b => b.RemainingSeconds).DefaultIfEmpty().Min(),
                elapsed) { ObservedActiveBuffs = observedBuffs, Stamina = stamina, Cast = cast,
                    UnmappedSignals = cast is null ? new[] { "Casting", "ActionBlock", "SubstitutionCooldownRemaining", "Threat", "AttackerIdentity", "QSCostCooldownAndEffect" }
                        : new[] { "ActionBlock", "SubstitutionCooldownRemaining", "Threat", "AttackerIdentity", "QSCostCooldownAndEffect" } };
            Status = $"OBSERVED [{Profile.Id}] {name} → {targetName ?? "no target"} · {elapsed} ms";
            return snapshot;
        }
        catch (Exception ex)
        {
            MapPlayers = null;
            Status = $"UNAVAILABLE: {ex.Message}";
            return null;
        }
    }

    private int StaticInt(ClrType type, string name, ClrAppDomain domain)
    {
        var field = type.GetStaticFieldByName(name) ?? throw new InvalidOperationException($"Missing {name}");
        if (field.Type?.Name != "System.Int32" || !field.IsInitialized(domain)) throw new InvalidOperationException($"Invalid {name}");
        var address = field.GetAddress(domain);
        if (address == 0 || !_target.DataReader.Read(address, out int value)) throw new InvalidOperationException($"Cannot read {name}");
        return value;
    }

    private (int PlayerIndex, int RawIndex, int Kind, ulong ObjectAddress) ReadSelectedTarget(ClrType globals, ClrAppDomain domain)
    {
        if (Profile.SelectedTarget is not { } fields)
        {
            var index = StaticInt(globals, Profile.SelectedIndex, domain);
            return (index, index, 0, 0);
        }
        var field = globals.GetStaticFieldByName(fields.GlobalObject) ?? throw new InvalidOperationException("Target object unavailable");
        if (!field.IsInitialized(domain) || field.GetAddress(domain) == 0) throw new InvalidOperationException("Target state uninitialized");
        if (field.Type is null || field.Type.IsValueType) throw new InvalidOperationException("Target schema changed");
        var obj = field.ReadObject(domain);
        if (obj.IsNull) return (0, 0, 0, 0);
        if (obj.Type?.Name != field.Type.Name) throw new InvalidOperationException("Target identity changed during read");
        var kind = Field<int>(obj, fields.Kind);
        var rawIndex = Field<int>(obj, fields.Index);
        return (fields.PlayerIndex(kind, rawIndex), rawIndex, kind, obj.Address);
    }

    private int ReadMapIdentity(ClrType roots, ClrAppDomain domain, ClrObject mapObject) => Profile.MapIdentityInRoots
        ? StaticInt(roots, Profile.MapIdentity, domain) : Field<int>(mapObject, Profile.MapIdentity);

    private NavigationFrame ReadTerrain(ClrType roots, ClrAppDomain domain, int localIndex, int map, double x, double y, long captured)
    {
        var mapField = roots.GetStaticFieldByName(Profile.MapRoot) ?? throw new InvalidOperationException("Map root unavailable");
        var mapObject = mapField.ReadObject(domain);
        if (mapObject.IsNull || ReadMapIdentity(roots, domain, mapObject) != map)
            throw new InvalidOperationException("Map identity mismatch");
        var tilesObject = mapObject.ReadObjectField(Profile.Tiles);
        if (!tilesObject.IsArray || tilesObject.Type?.Name != "Common.TileRec[,]") throw new InvalidOperationException("Tile schema unavailable");
        var tiles = tilesObject.AsArray();
        if (tiles.Rank != 2 || tiles.GetLowerBound(0) != 0 || tiles.GetLowerBound(1) != 0 ||
            tiles.GetLength(0) is < 1 or > 512 || tiles.GetLength(1) is < 1 or > 512)
            throw new InvalidOperationException("Tile dimensions invalid");
        var px = (int)Math.Floor(x); var py = (int)Math.Floor(y);
        if (px < 0 || py < 0 || px >= tiles.GetLength(0) || py >= tiles.GetLength(1)) throw new InvalidOperationException("Player outside grid");
        var originX = Math.Max(0, px - 12); var originY = Math.Max(0, py - 12);
        var width = Math.Min(tiles.GetLength(0), px + 13) - originX;
        var height = Math.Min(tiles.GetLength(1), py + 13) - originY;
        var cells = new NavigationCell[width * height];
        var playerTile = tiles.GetObjectValue(new[] { px, py });
        if (playerTile.IsNull) throw new InvalidOperationException("Local tile unknown");
        var playerHeight = Field<int>(playerTile, "Height");
        for (var gx = originX; gx < originX + width; gx++)
            for (var gy = originY; gy < originY + height; gy++)
            {
                var tile = tiles.GetObjectValue(new[] { gx, gy });
                if (tile.IsNull) continue;
                var type = Field<byte>(tile, "Type");
                var blocked = Field<byte>(tile, "DirBlock") != 0 || Field<int>(tile, "Height") != playerHeight;
                // Values extracted from the fingerprinted Common.TileAttributes enum, not guessed from pixels.
                cells[(gy - originY) * width + gx - originX] = blocked ? NavigationCell.Blocked : type switch
                {
                    0 or 4 or 18 or 19 or 21 or 22 => NavigationCell.Open,
                    13 or 14 or 17 or 20 or 25 => NavigationCell.Hazard,
                    _ => NavigationCell.Blocked // includes warps, unknown types, resources/objects, stairs and ramps
                };
            }
        var projectileField = roots.GetStaticFieldByName(Profile.Projectiles) ?? throw new InvalidOperationException("Projectile root unavailable");
        var projectileObject = projectileField.ReadObject(domain);
        if (!projectileObject.IsArray || projectileObject.Type?.Name != "client.ProjectileRec[]")
            throw new InvalidOperationException("Projectile schema unavailable");
        var projectiles = projectileObject.AsArray();
        if (projectiles.Length is < 1 or > 8192) throw new InvalidOperationException("Projectile count invalid");
        var hazards = 0; var ignoredOwn = 0;
        var definitions = new Dictionary<int, ProjectileSourceDefinition?>();
        var spellsObject = roots.StaticFields.SingleOrDefault(f => f.Type?.Name == "client.SpellRec[]")?.ReadObject(domain);
        for (var i = 0; i < projectiles.Length; i++)
        {
            var projectile = projectiles.GetObjectValue(i);
            if (projectile.IsNull) continue;
            var owner = Field<int>(projectile, "Owner");
            if (owner == 0) continue;
            // Legacy trap packets populate X/Y without HasAuthoritativeSnapshot; that flag is NOT a liveness marker.
            // Both packet variants were observed on real Bear Traps / Triple Explosive Tags during calibration.
            // Bear Trap calibration: X/Y are map-tile coordinates, unlike player PixelX/PixelY.
            var hx = Field<double>(projectile, "X"); var hy = Field<double>(projectile, "Y");
            if (!double.IsFinite(hx) || !double.IsFinite(hy) || Math.Abs(hx) > 100000 || Math.Abs(hy) > 100000)
                throw new InvalidOperationException("Projectile position invalid");
            var extent = Math.Max(Field<int>(projectile, "HitboxWidth"), Field<int>(projectile, "HitboxLength"));
            if (extent is < 0 or > 2048) throw new InvalidOperationException("Projectile hitbox unknown");
            if (owner == localIndex && Field<int>(projectile, "OwnerType") == 1)
            {
                var spellId = Field<int>(projectile, "Spell");
                if (!definitions.TryGetValue(spellId, out var source))
                {
                    source = null;
                    try
                    {
                        if (spellsObject is { IsArray: true } spellsRoot)
                        {
                            var spells = spellsRoot.AsArray();
                            if (spellId > 0 && spellId < spells.Length)
                            {
                                var spell = spells.GetObjectValue(spellId);
                                if (!spell.IsNull && spell.Type?.Name == "client.SpellRec")
                                {
                                    var prototype = spell.ReadObjectField("Projectile");
                                    var sourceName = spell.ReadStringField("Name", 128);
                                    if (!prototype.IsNull && sourceName is not null)
                                        source = new(sourceName, Field<byte>(spell, "Type"), Field<bool>(spell, "WalkCast"), Field<int>(prototype, "pic"));
                                }
                            }
                        }
                    }
                    catch { source = null; } // Unreadable/unknown source remains a hazard, never a safety exemption.
                    definitions[spellId] = source;
                }
                if (ProjectileHazardPolicy.IgnoreOwnAttack(localIndex, owner, 1, Field<int>(projectile, "Range"),
                    Field<int>(projectile, "Speed"), Field<int>(projectile, "pic"), source))
                { ignoredOwn++; continue; }
            }
            var padding = Math.Clamp((int)Math.Ceiling(extent / 64.0), 1, 16);
            hazards++;
            for (var gx = (int)Math.Floor(hx) - padding; gx <= (int)Math.Floor(hx) + padding; gx++)
                for (var gy = (int)Math.Floor(hy) - padding; gy <= (int)Math.Floor(hy) + padding; gy++)
                    if (gx >= originX && gy >= originY && gx < originX + width && gy < originY + height)
                        cells[(gy - originY) * width + gx - originX] = NavigationCell.Hazard;
        }
        if (mapField.ReadObject(domain).Address != mapObject.Address ||
            mapObject.ReadObjectField(Profile.Tiles).Address != tilesObject.Address ||
            ReadMapIdentity(roots, domain, mapObject) != map || projectileField.ReadObject(domain).Address != projectileObject.Address)
            throw new InvalidOperationException("Terrain changed during read");
        return new(map, captured, originX, originY, width, height, cells, true, hazards) { IgnoredOwnShots = ignoredOwn };
    }

    private static T Field<T>(ClrObject obj, string name) where T : unmanaged
    {
        if (obj.Type?.GetFieldByName(name)?.Type?.Name != typeof(T).FullName || !obj.TryReadField<T>(name, out var value))
            throw new InvalidOperationException($"Field schema/read mismatch: {name}");
        return value;
    }

    private static int[] IntArray(ClrObject obj, string name, int expectedLength)
    {
        var field = obj.ReadObjectField(name);
        if (field.IsNull || !field.IsArray || field.Type?.Name != "System.Int32[]") throw new InvalidOperationException($"Missing {name}");
        var array = field.AsArray();
        if (array.Length != expectedLength) throw new InvalidOperationException($"Unexpected {name} length");
        return array.ReadValues<int>(0, expectedLength) ?? throw new InvalidOperationException($"Cannot read {name}");
    }

    private static double Coordinate(ClrObject obj, string name)
    {
        var value = Field<float>(obj, name);
        if (!float.IsFinite(value) || value < -32 || value > 1_000_000) throw new InvalidOperationException("Invalid coordinates");
        return value / LiveMemoryProfile.PixelsPerTile;
    }

    public static Direction DecodeFacing(byte value) => value switch
    {
        0 => Direction.Up, 1 => Direction.Down, 2 => Direction.Left, 3 => Direction.Right,
        // Facing-dependent Turn/Tool decisions remain cardinal; diagonal movement is handled separately.
        _ => Direction.None
    };

    public void Dispose() { Frame = null; _runtime.Dispose(); _target.Dispose(); _process.Dispose(); }
}
