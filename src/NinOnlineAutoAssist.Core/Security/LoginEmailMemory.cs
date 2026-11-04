using System.Net.Mail;
using System.Text.Json;

namespace NinOnlineAutoAssist.Core.Security;

/// <summary>Local convenience only. This file never contains passwords, tokens or authorization state.</summary>
public sealed class LoginEmailMemory(string path)
{
    private sealed record Preference(string LastEmail);
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NinOnlineAutoAssist", "login-preferences.json");

    private static string Normalize(string? email)
    {
        var value = email?.Trim() ?? "";
        return value.Length is > 0 and <= 254 && !value.Any(char.IsControl) &&
            MailAddress.TryCreate(value, out var parsed) && parsed.Address.Equals(value, StringComparison.OrdinalIgnoreCase)
            ? value : "";
    }
    public string Read()
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return "";
            return Normalize(JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.LastEmail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return ""; }
    }
    public bool TryRemember(string email)
    {
        var value = Normalize(email);
        if (value.Length == 0) return false;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Preference(value)));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    public bool TryForget()
    {
        try { File.Delete(path); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }
}
