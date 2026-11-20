namespace NinOnlineAutoAssist.Live;

public enum EarlyPunishState
{
	Idle,
	WatchingBuff,
	RemovalPending,
	Executing,
	Completed,
	Expired,
	Cancelled
}
