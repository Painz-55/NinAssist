using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Passive calibration only. Byte candidates are NOT assumed to be village fields.</summary>
public static class VillageProbe
{
    public static int Run(int pid, string character)
    {
        using var provider = new ReadOnlyGameStateProvider(pid, character, new SystemMonotonicClock());
        if (provider.GetSnapshot() is null) { Console.Error.WriteLine(provider.Status); return 1; }
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var profile = provider.Profile;
        var roots = module.GetTypeByName(profile.Roots)!;
        var domain = runtime.AppDomains.Single();
        var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
        var rows = new List<object>();
        for (var i = 1; i < players.Length; i++)
        {
            try
            {
                var entity = players.GetObjectValue(i);
                if (entity.IsNull || entity.ReadField<int>(profile.Map) != provider.Frame!.Map) continue;
                var name = entity.ReadStringField(profile.Name, 64);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var fields = new Dictionary<string, byte>();
                foreach (var field in entity.Type!.Fields.Where(f => f.Type?.Name == "System.Byte"))
                    fields[field.Name!] = field.Read<byte>(entity.Address, false);
                if (players.GetObjectValue(i).Address != entity.Address || entity.ReadStringField(profile.Name, 64) != name ||
                    entity.ReadField<int>(profile.Map) != provider.Frame.Map) continue;
                rows.Add(new { Slot = i, Name = name, ByteCandidates = fields });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
            { /* Transient reads are discarded, not inferred. */ }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { profile.Id, provider.Frame!.Map, Players = rows },
            new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
