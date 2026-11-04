using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive calibration only: identity/build validation, root schemas and terrain enum constants.</summary>
public static class TerrainProbe
{
    public static int Run(int pid, string character)
    {
        using var provider = new ReadOnlyGameStateProvider(pid, character, new SystemMonotonicClock());
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var common = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "Common.dll");
        var profile = provider.Profile;
        var roots = module.GetTypeByName(profile.Roots)!;
        var tile = common.GetTypeByName("Common.TileRec");
        var mapType = roots.GetStaticFieldByName(profile.MapRoot)?.Type
            ?? throw new InvalidOperationException("Map type unavailable");
        var domain = runtime.AppDomains.Single();
        var projectiles = new List<object>();
        var projectileArray = roots.GetStaticFieldByName(profile.Projectiles)!.ReadObject(domain).AsArray();
        var spells = roots.StaticFields.Single(f => f.Type?.Name == "client.SpellRec[]").ReadObject(domain).AsArray();
        for (var i = 0; i < Math.Min(projectileArray.Length, 8192); i++)
        {
            var projectile = projectileArray.GetObjectValue(i);
            if (projectile.IsNull || projectile.ReadField<int>("Owner") == 0) continue;
            var spell = projectile.ReadField<int>("Spell");
            var spellName = spell > 0 && spell < spells.Length ? spells.GetObjectValue(spell).ReadStringField("Name", 128) : null;
            projectiles.Add(new { Index = i, SpellName = spellName, Values = PrimitiveValues(projectile),
                Fields = projectile.Type!.Fields.Select(f => new { f.Name, Type = f.Type?.Name }) });
        }
        var rootFields = roots.StaticFields.Select(f => new { f.Name, Type = f.Type?.Name }).ToArray();
        var collections = new List<object>();
        foreach (var field in roots.StaticFields.Where(f => f.Type?.Name?.Contains("ConcurrentDictionary") == true ||
            f.Type?.Name?.Contains("HashSet") == true || (f.Type?.Name?.EndsWith("[]") == true &&
                f.Type.Name.StartsWith("System.") == false && f.Type.Name.StartsWith("Common.") == false &&
                f.Name != profile.Players)))
        {
            if (!field.IsInitialized(domain)) continue;
            var obj = field.ReadObject(domain);
            if (obj.IsNull) continue;
            var samples = new List<object>();
            if (obj.Type?.Name?.Contains("ConcurrentDictionary") == true)
            {
                var tables = obj.ReadObjectField("_tables");
                var buckets = tables.ReadObjectField("_buckets").AsArray();
                for (var i = 0; i < Math.Min(4096, buckets.Length) && samples.Count < 8; i++)
                {
                    var node = buckets.GetStructValue(i).ReadObjectField("_node");
                    for (var chain = 0; !node.IsNull && chain < 32 && samples.Count < 8; chain++)
                    {
                        var valueField = node.Type!.GetFieldByName("_value")!;
                        if (valueField.Type?.IsObjectReference == true)
                        {
                            var value = node.ReadObjectField("_value");
                            if (!value.IsNull) samples.Add(new { Type = value.Type?.Name,
                                Fields = value.Type?.Fields.Select(f => new { f.Name, Type = f.Type?.Name }), Values = PrimitiveValues(value) });
                        }
                        else
                        {
                            var value = node.ReadValueTypeField("_value");
                            samples.Add(new { Type = value.Type?.Name,
                                Fields = value.Type?.Fields.Select(f => new { f.Name, Type = f.Type?.Name }),
                                Values = value.Type?.Fields.Where(f => f.Type?.Name == "System.Int32")
                                    .ToDictionary(f => f.Name!, f => value.ReadField<int>(f.Name!)) });
                        }
                        node = node.ReadObjectField("_next");
                    }
                }
            }
            if (obj.IsArray)
            {
                var array = obj.AsArray();
                for (var i = 0; i < Math.Min(2000, array.Length) && samples.Count < 5; i++)
                {
                    if (obj.Type!.ComponentType?.IsValueType == true)
                    {
                        var value = array.GetStructValue(i);
                        var values = value.Type?.Fields.Where(f => f.Type?.Name == "System.Int32")
                            .ToDictionary(f => f.Name!, f => value.ReadField<int>(f.Name!));
                        if (values?.Values.All(v => v == 0) == true) continue;
                        samples.Add(new { Index = i, Type = value.Type?.Name,
                            Fields = value.Type?.Fields.Select(f => new { f.Name, Type = f.Type?.Name }), Values = values });
                    }
                    else
                    {
                        var item = array.GetObjectValue(i);
                        if (item.IsNull) continue;
                        samples.Add(new { Index = i, Type = item.Type?.Name,
                            Fields = item.Type?.Fields.Select(f => new { f.Name, Type = f.Type?.Name }), Values = PrimitiveValues(item) });
                    }
                }
            }
            collections.Add(new { field.Name, Type = obj.Type?.Name,
                Fields = obj.Type?.Fields.Select(f => new { f.Name, Type = f.Type?.Name }), Samples = samples });
        }
        var mapRoots = new List<object>();
        foreach (var field in roots.StaticFields.Where(f => f.Type?.Name == mapType?.Name))
        {
            if (!field.IsInitialized(domain)) continue;
            var obj = field.ReadObject(domain);
            if (obj.IsNull) continue;
            var tilesObject = obj.ReadObjectField(profile.Tiles);
            var cells = new List<object>();
            var grid = tilesObject.AsArray();
            var px = (int)Math.Floor(provider.Frame!.Snapshot.Player.PositionX);
            var py = (int)Math.Floor(provider.Frame.Snapshot.Player.PositionY);
            for (var x = Math.Max(0, px - 10); x <= Math.Min(grid.GetLength(0) - 1, px + 10); x++)
                for (var y = Math.Max(0, py - 10); y <= Math.Min(grid.GetLength(1) - 1, py + 10); y++)
                {
                    var cell = grid.GetObjectValue(new[] { x, y });
                    if (cell.IsNull) continue;
                    cells.Add(new { X = x, Y = y, Type = cell.ReadField<byte>("Type"), DirBlock = cell.ReadField<byte>("DirBlock"), Height = cell.ReadField<int>("Height") });
                }
            mapRoots.Add(new { field.Name, ArrayType = tilesObject.Type?.Name,
                Length = tilesObject.IsArray ? tilesObject.AsArray().Length : 0,
                Width = grid.GetLength(0), Height = grid.GetLength(1), Cells = cells, Values = PrimitiveValues(obj),
                Fields = obj.Type!.Fields.Select(f => new { f.Name, Type = f.Type?.Name }) });
        }
        using var stream = File.OpenRead(Path.Combine(provider.GameDirectory, "Common.dll"));
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var enumValues = metadata.TypeDefinitions.Select(h => metadata.GetTypeDefinition(h))
            .Where(t => metadata.GetString(t.Name) == "TileAttributes")
            .SelectMany(t => t.GetFields()).Select(h => metadata.GetFieldDefinition(h))
            .Where(f => !f.GetDefaultValue().IsNil).Select(f =>
            {
                var constant = metadata.GetConstant(f.GetDefaultValue());
                return new { Name = metadata.GetString(f.Name), Kind = constant.TypeCode.ToString(),
                    ValueHex = Convert.ToHexString(metadata.GetBlobBytes(constant.Value)) };
            }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            provider.Status, provider.Frame, RootFields = rootFields, MapRoots = mapRoots,
            Collections = collections,
            Projectiles = projectiles,
            ArrayApi = typeof(ClrArray).GetMethods().Where(m => m.DeclaringType == typeof(ClrArray)).Select(m => m.ToString()),
            TileFields = tile?.Fields.Select(f => new { f.Name, Type = f.Type?.Name, f.Offset }), TileAttributes = enumValues
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static Dictionary<string, object?> PrimitiveValues(ClrObject obj)
    {
        var result = new Dictionary<string, object?>();
        foreach (var field in obj.Type!.Fields)
            result[field.Name!] = field.Type?.Name switch
            {
                "System.Int32" => obj.ReadField<int>(field.Name!),
                "System.Byte" => obj.ReadField<byte>(field.Name!),
                "System.Single" => obj.ReadField<float>(field.Name!),
                "System.Double" => obj.ReadField<double>(field.Name!),
                "System.Boolean" => obj.ReadField<bool>(field.Name!),
                _ => null
            };
        return result;
    }
}
