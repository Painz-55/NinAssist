namespace NinOnlineAutoAssist.Live;

public static class PlayerVillages
{
	public static bool IsKnown(PlayerVillage village)
	{
		if ((uint)(village - 1) <= 2u || village == PlayerVillage.Renegade)
		{
			return true;
		}
		return false;
	}

	public static PlayerVillage FromCode(byte? code)
	{
		switch (code)
		{
		case 1:
			return PlayerVillage.Leaf;
		case 2:
			return PlayerVillage.Sand;
		case 3:
			return PlayerVillage.Mist;
		case 11:
		case 12:
		case 13:
		case 14:
		case 15:
		case 16:
		case 17:
		case 18:
		case 19:
		case 20:
			return PlayerVillage.Renegade;
		default:
			return PlayerVillage.Unknown;
		}
	}

	public static string Label(PlayerVillage village)
	{
		return village switch
		{
			PlayerVillage.Leaf => "Leaf",
			PlayerVillage.Sand => "Sand",
			PlayerVillage.Mist => "Mist",
			PlayerVillage.Renegade => "Renegado",
			_ => "Desconhecida",
		};
	}

	public static int? ColorArgb(PlayerVillage village)
	{
		return village switch
		{
			PlayerVillage.Leaf => -6765703,
			PlayerVillage.Sand => -1720197,
			PlayerVillage.Mist => -10375185,
			PlayerVillage.Renegade => -2069387,
			_ => null,
		};
	}
}
