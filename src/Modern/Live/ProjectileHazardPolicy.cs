using System;
using System.Collections.Generic;

namespace NinOnlineAutoAssist.Live;

public static class ProjectileHazardPolicy
{
	private static readonly HashSet<string> OwnAttackNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"Shuriken", "Fan I Basic", "Fan II DMG", "Fan III DMG Range", "Fan IV Triple", "Fan V Long Pierce", "Fan VI Crystal", "Fan VII Life Steal", "Fan VIII Triple Short", "Fan V Tornado",
		"Fan II DMG (Knockbacks)(Hyaku)", "Fan VII Double Hit", "Fan VII Double Hit (2)", "Fan Jinbu Double Hit", "Fan Jinbu Double Hit (2)"
	};

	public static bool IgnoreOwnAttack(int localIndex, int owner, int ownerType, int range, int speed, int picture, ProjectileSourceDefinition? source)
	{
		if (localIndex > 0 && owner == localIndex && ownerType == 1 && range > 0 && range <= 64 && speed > 0 && speed <= 1000 && (object)source != null && source.Type == 4 && source.WalkCast && source.Picture == picture)
		{
			return OwnAttackNames.Contains(source.Name);
		}
		return false;
	}
}
