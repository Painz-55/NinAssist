namespace NinOnlineAutoAssist.Live;

public sealed record StaminaObservation(int Current, int Maximum, bool Running)
{
	public bool Valid
	{
		get
		{
			if (Maximum > 0 && Current >= 0)
			{
				return Current <= Maximum;
			}
			return false;
		}
	}

	public double Percent
	{
		get
		{
			if (!Valid)
			{
				return double.NaN;
			}
			return (double)Current * 100.0 / (double)Maximum;
		}
	}
}
