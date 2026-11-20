namespace NinOnlineAutoAssist.Live;

public sealed record SelectedTargetMemoryFields(string GlobalObject, string Kind, string Index, int PlayerKind)
{
	public int PlayerIndex(int kind, int index)
	{
		if (kind != PlayerKind || index <= 0)
		{
			return 0;
		}
		return index;
	}
}
