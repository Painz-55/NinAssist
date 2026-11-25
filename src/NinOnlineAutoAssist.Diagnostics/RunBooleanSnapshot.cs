using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Single read-only sample of boolean candidates for a manually controlled Run state.</summary>
public static class RunBooleanSnapshot
{
    public static int Run(int pid, string character, string profilePath)
    {
        var profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(profilePath))
            ?? throw new InvalidOperationException("Missing calibration profile");
        if (!profile.ExternalProfile || profile.ActuationValidated || profile.ExperimentalTrial ||
            profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0)
            throw new InvalidOperationException("Input-disabled local calibration required");
        using var process = Process.GetProcessById(pid);
        if (process.ProcessName != "NinOnline" || character.Length is < 1 or > 64)
            throw new InvalidOperationException("Unexpected process or character");
        var directory = Path.GetDirectoryName(process.MainModule!.FileName)!;
        static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        if (!Hash(Path.Combine(directory, "NinOnline.dll")).Equals(profile.Sha256, StringComparison.OrdinalIgnoreCase) ||
            !Hash(Path.Combine(directory, "Common.dll")).Equals(profile.CommonSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DLL pair changed");
        using var target = DataTarget.AttachToProcess(pid, suspend: false);
        using var runtime = target.ClrVersions.Single().CreateRuntime();
        runtime.FlushCachedData();
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var domain = runtime.AppDomains.Single();
        var roots = module.GetTypeByName(profile.Roots)!;
        var globals = module.GetTypeByName(profile.Globals)!;
        int slot = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
        var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
        if (slot <= 0 || slot >= players.Length) throw new InvalidOperationException("Local slot invalid");
        var actor = players.GetObjectValue(slot);
        if (actor.IsNull || actor.ReadStringField(profile.Name, 64) != character)
            throw new InvalidOperationException("Local character changed");
        var values = new SortedDictionary<string, bool>();
        foreach (var field in globals.StaticFields.Where(f => f.IsInitialized(domain) && f.Type?.Name == "System.Boolean"))
            values["G:" + field.Name] = field.Read<bool>(domain);
        foreach (var field in actor.Type!.Fields.Where(f => f.Type?.Name == "System.Boolean"))
            values["A:" + field.Name] = field.Read<bool>(actor.Address, false);
        if (globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain) != slot ||
            players.GetObjectValue(slot).Address != actor.Address || actor.ReadStringField(profile.Name, 64) != character)
            throw new InvalidOperationException("Identity changed during sample");
        Console.WriteLine(JsonSerializer.Serialize(new { profile.Sha256, profile.CommonSha256, Slot = slot,
            Character = character, Values = values,
            Warning = "Manual Run state must be confirmed separately; candidate values authorize no input." }));
        return 0;
    }
}
