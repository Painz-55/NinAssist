namespace NinOnlineAutoAssist.Live;

public sealed record MapPlayer(int Slot, string Name, int Map, double? X, double? Y, bool IsLocal, bool IsSelected)
{
	public PlayerVillage Village { get; init; }

	// An unreadable HP is not proof that a player is alive.
	public bool IsAlive { get; init; }
}
