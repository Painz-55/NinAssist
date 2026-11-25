using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Diagnostics;

/// <summary>Read-only CLR and raw-byte comparison for the selected target object.</summary>
public static class TargetLayoutSnapshot
{
    public static int Run(int pid, string character, string profilePath, int seconds = 0)
    {
        if (seconds is < 0 or > 30) throw new ArgumentOutOfRangeException(nameof(seconds));
        var profile = JsonSerializer.Deserialize<GameMemoryProfile>(File.ReadAllText(profilePath))
            ?? throw new InvalidOperationException("Missing calibration profile");
        if (!profile.ExternalProfile || profile.ActuationValidated || profile.ExperimentalTrial ||
            profile.ValidatedInputKinds.Length != 0 || profile.ValidatedAttackModes.Length != 0 ||
            profile.SelectedTarget is not { } selected)
            throw new InvalidOperationException("Input-disabled target calibration required");
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
        var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
        var globals = module.GetTypeByName(profile.Globals)!;
        var roots = module.GetTypeByName(profile.Roots)!;
        var observations = new List<object>();
        string? previous = null;
        int targetRaces = 0;
        var watch = Stopwatch.StartNew();
        do
        {
        try
        {
        runtime.FlushCachedData();
        var domain = runtime.AppDomains.Single();
        int local = globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain);
        var players = roots.GetStaticFieldByName(profile.Players)!.ReadObject(domain).AsArray();
        if (local <= 0 || local >= players.Length || players.GetObjectValue(local).ReadStringField(profile.Name, 64) != character)
            throw new InvalidOperationException("Local character changed");
        var selectedField = globals.GetStaticFieldByName(selected.GlobalObject)!;
        var obj = selectedField.ReadObject(domain);
        if (obj.IsNull || obj.Type?.Name != selectedField.Type?.Name)
            throw new InvalidOperationException("Selected object unavailable");
        var fields = obj.Type!.Fields.Where(f => f.Type?.Name == "System.Int32")
            .Select(f => new { f.Name, f.Offset, Value = f.Read<int>(obj.Address, false) }).ToArray();
        byte[] bytes = new byte[32];
        if (target.DataReader.Read(obj.Address + (ulong)IntPtr.Size, bytes) != bytes.Length)
            throw new InvalidOperationException("Raw target bytes unavailable");
        if (globals.GetStaticFieldByName(profile.LocalIndex)!.Read<int>(domain) != local ||
            players.GetObjectValue(local).ReadStringField(profile.Name, 64) != character)
            throw new InvalidOperationException("Local identity changed during sample");
        if (selectedField.ReadObject(domain).Address != obj.Address ||
            fields.Any(f => obj.ReadField<int>(f.Name!) != f.Value))
            throw new TargetChangedException();
        // This observation is deliberately not a writer: both CLR fields and raw bytes
        // must agree before a future, separately authorized layout can be considered.
        bool contiguousPairs = fields.Length == 4 && fields[0].Name == selected.Kind &&
            fields[1].Name == selected.Index && fields.Select(f => f.Offset).SequenceEqual(new[] { 0, 4, 8, 12 });
        bool rawAgrees = contiguousPairs && fields.All(f => BitConverter.ToInt32(bytes, f.Offset) == f.Value);
        bool mirrored = rawAgrees && fields[0].Value == fields[2].Value && fields[1].Value == fields[3].Value;
        var observation = new { AtMs = watch.ElapsedMilliseconds, profile.Sha256, LocalSlot = local,
            RecordType = obj.Type.Name, Fields = fields, RawAfterHeader = Convert.ToHexString(bytes),
            SelectedKind = obj.ReadField<int>(selected.Kind), SelectedIndex = obj.ReadField<int>(selected.Index),
            ContiguousPairs = contiguousPairs, RawAgreesWithClr = rawAgrees, PairsMirrored = mirrored,
            Warning = "Read-only offset evidence only; no target memory write authorized." };
        string marker = $"{local}:{observation.RawAfterHeader}:{contiguousPairs}:{rawAgrees}:{mirrored}";
        if (marker != previous) observations.Add(observation);
        previous = marker;
        }
        catch (TargetChangedException) when (seconds > 0) { targetRaces++; previous = null; }
        if (seconds > 0) Thread.Sleep(100);
        } while (seconds > 0 && watch.Elapsed < TimeSpan.FromSeconds(seconds));
        Console.WriteLine(JsonSerializer.Serialize(new { Observations = observations, TargetRaces = targetRaces,
            Warning = "Read-only transitions; no input or write approval." }));
        return 0;
    }

    private sealed class TargetChangedException : Exception { }
}
