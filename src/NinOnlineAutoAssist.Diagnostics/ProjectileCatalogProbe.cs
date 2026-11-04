using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

public static class ProjectileCatalogProbe
{
    public static int Run(int pid, string character)
    {
        using var provider = new ReadOnlyGameStateProvider(pid, character, new SystemMonotonicClock());
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var roots = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll").GetTypeByName(provider.Profile.Roots)!;
        var domain = runtime.AppDomains.Single(); var rows = new List<object>();
        foreach (var kind in new[] { "client.SpellRec[]", "client.ItemRec[]" })
        {
            var array = roots.StaticFields.Single(f => f.Type?.Name == kind).ReadObject(domain).AsArray();
            for (var i = 1; i < array.Length; i++)
            {
                var item = array.GetObjectValue(i); if (item.IsNull) continue;
                var name = item.ReadStringField("Name", 128);
                if (name is null || !(name.Contains("shuriken", StringComparison.OrdinalIgnoreCase) || name.Contains("fan", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("hisui", StringComparison.OrdinalIgnoreCase) || name.Contains("hisuy", StringComparison.OrdinalIgnoreCase) || (kind == "client.SpellRec[]" && i is 8 or 235 or 241))) continue;
                var p = item.ReadObjectField("Projectile");
                var projectile = new Dictionary<string, object?>();
                if (!p.IsNull) foreach (var field in new[] { "Spell", "Ammo", "Range", "Speed", "pic", "HitboxWidth", "HitboxLength" }) projectile[field] = p.ReadField<int>(field);
                rows.Add(new { Kind = kind, Index = i, Name = name, Type = item.ReadField<byte>("Type"),
                    CastTime = kind == "client.SpellRec[]" ? item.ReadField<float>("CastTime") : (float?)null,
                    WalkCast = kind == "client.SpellRec[]" ? item.ReadField<bool>("WalkCast") : (bool?)null, Projectile = projectile });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true })); return 0;
    }
}
