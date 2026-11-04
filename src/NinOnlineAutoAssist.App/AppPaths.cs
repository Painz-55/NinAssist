using NinOnlineAutoAssist.Core.Config;
using Velopack.Locators;

namespace NinOnlineAutoAssist.App;

internal static class AppPaths
{
    public static bool UpdaterManaged => VelopackLocator.Current.CurrentlyInstalledVersion is not null;
    public static UserSettingsStorage Settings { get; } = new(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), UpdaterManaged);
}
