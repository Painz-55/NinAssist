using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public static class SubstitutionBuffs
{
	public const string Normal = "Substitution Technique";

	public const string Water = "Water Substitution Technique";

	public const string Tai = "Speed Mirage Technique";

	public static bool IsKnown(string name)
	{
		switch (name)
		{
		case "Substitution Technique":
		case "Water Substitution Technique":
		case "Speed Mirage Technique":
			return true;
		default:
			return false;
		}
	}

	public static bool IsPunishEligible(string name)
	{
		if (name == "Water Substitution Technique" || name == "Speed Mirage Technique")
		{
			return true;
		}
		return false;
	}

	public static string Label(string name)
	{
		return name switch
		{
			"Substitution Technique" => "Normal",
			"Water Substitution Technique" => "Água",
			"Speed Mirage Technique" => "Tai",
			_ => name,
		};
	}

	public static IReadOnlyList<ObservedBuff> Active(LiveFrame frame)
	{
		if (frame.ObservedActiveBuffs.Count > 0)
		{
			return frame.ObservedActiveBuffs.Where((ObservedBuff b) => IsKnown(b.Name)).ToArray();
		}
		if (!frame.SubstitutionActive)
		{
			return Array.Empty<ObservedBuff>();
		}
		return new ObservedBuff[1]
		{
			new ObservedBuff("Substitution Technique", frame.SubstitutionDuration)
		};
	}
}
