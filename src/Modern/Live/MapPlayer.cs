namespace NinOnlineAutoAssist.Live;

public sealed record MapPlayer(int Slot, string Name, int Map, double? X, double? Y, bool IsLocal, bool IsSelected)
{
	public PlayerVillage Village { get; init; }
}
