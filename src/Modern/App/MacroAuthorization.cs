namespace NinOnlineAutoAssist.App;

// Authorization is derived from the current ON action, never from saved character settings.
internal static class MacroAuthorization
{
	public static bool SubPunish(bool enabled, bool permitted, bool comboValidated) =>
		enabled && permitted && comboValidated;

	public static bool Combat(bool enabled, bool attackPermitted, bool toolPermitted) =>
		enabled && (attackPermitted || toolPermitted);
}
