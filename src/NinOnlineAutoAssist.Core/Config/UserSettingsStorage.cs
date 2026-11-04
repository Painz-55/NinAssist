namespace NinOnlineAutoAssist.Core.Config;

/// <summary>Updater-managed installations must not store personal data inside the replaced application folder.</summary>
public sealed class UserSettingsStorage(string applicationDirectory, string localApplicationData, bool updaterManaged)
{
    public string DataDirectory => Path.Combine(localApplicationData, "NinOnlineAutoAssist");
    public string ConfigPath => Path.Combine(updaterManaged ? DataDirectory : applicationDirectory, "ninautoassist.config.json");
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
    public string ResolveLogPath(string path) => updaterManaged && !Path.IsPathRooted(path) ? Path.Combine(DataDirectory, path) : path;
}
