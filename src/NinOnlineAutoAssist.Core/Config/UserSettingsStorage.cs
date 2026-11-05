namespace NinOnlineAutoAssist.Core.Config;

/// <summary>Updater-managed installations must not store personal data inside the replaced application folder.</summary>
public sealed class UserSettingsStorage(string applicationDirectory, string localApplicationData, bool updaterManaged)
{
    public string DataDirectory => Path.Combine(localApplicationData, "NinOnlineAutoAssist");
    public string ConfigPath => Path.Combine(updaterManaged ? DataDirectory : applicationDirectory, "ninautoassist.config.json");
    public string CharacterConfigPath(string verifiedName)
    {
        if (string.IsNullOrWhiteSpace(verifiedName) || verifiedName.Length > 128 || verifiedName.Any(char.IsControl))
            throw new ArgumentException("Identidade local inválida.", nameof(verifiedName));
        var normalized = verifiedName.Trim().ToUpperInvariant();
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(Path.GetDirectoryName(ConfigPath)!, "characters", digest + ".json");
    }
    public string EnsureCharacterConfig(string verifiedName)
    {
        var path = CharacterConfigPath(verifiedName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.Copy(ConfigPath, path, overwrite: false); // migrate once, never replace
        return path;
    }
    public void EnsureConfig()
    {
        if (File.Exists(ConfigPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ConfigPath))!);
        var bundled = Path.Combine(applicationDirectory, "ninautoassist.config.json");
        if (updaterManaged && File.Exists(bundled))
        {
            ConfigLoader.Load(bundled); // Reject broken JSON before importing; never overwrite an existing user file.
            File.Copy(bundled, ConfigPath, overwrite: false);
        }
        else ConfigLoader.SaveDefault(ConfigPath);
    }
    public string ResolveLogPath(string path) => Path.IsPathRooted(path) ? path
        : Path.Combine(updaterManaged ? DataDirectory : applicationDirectory, path);
}
