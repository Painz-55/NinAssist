using System;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public static class CombatGeometry
{
	public static Direction AttackDirection(LiveFrame frame, CombatAssistSettings settings)
	{
		if (settings.AttackMode != AutoAttackMode.Melee)
		{
			TargetInfo target = frame.Snapshot.Target;
			if ((object)target != null && target.IsAlive && target.IsTargeted && frame.Snapshot.Player.IsAlive)
			{
				Direction facing = frame.Snapshot.Player.Facing;
				if (settings.AttackMode != AutoAttackMode.Fan && InRange(frame, CombatAction.Attack, settings))
				{
					return facing;
				}
				double num = target.PositionX - frame.Snapshot.Player.PositionX;
				double num2 = target.PositionY - frame.Snapshot.Player.PositionY;
				if (!double.IsFinite(num) || !double.IsFinite(num2))
				{
					return Direction.None;
				}
				Direction[] array = ((!(Math.Abs(num) >= Math.Abs(num2))) ? new Direction[2]
				{
					(!(num2 > 0.0)) ? Direction.Up : Direction.Down,
					(num > 0.0) ? Direction.Right : Direction.Left
				} : new Direction[2]
				{
					(num > 0.0) ? Direction.Right : Direction.Left,
					(!(num2 > 0.0)) ? Direction.Up : Direction.Down
				});
				foreach (Direction direction in array)
				{
					if (InRange(frame with
					{
						Snapshot = frame.Snapshot with
						{
							Player = frame.Snapshot.Player with
							{
								Facing = direction
							}
						}
					}, CombatAction.Attack, settings))
					{
						return direction;
					}
				}
				return Direction.None;
			}
		}
		return Direction.None;
	}

	public static bool InRange(LiveFrame frame, CombatAction action, CombatAssistSettings settings)
	{
		if ((uint)action > 1u)
		{
			return false;
		}
		TargetInfo target = frame.Snapshot.Target;
		if ((object)target == null || !target.IsAlive || !target.IsTargeted || !frame.Snapshot.Player.IsAlive)
		{
			return false;
		}
		PlayerState player = frame.Snapshot.Player;
		double num = target.PositionX - player.PositionX;
		double num2 = target.PositionY - player.PositionY;
		if (!double.IsFinite(num) || !double.IsFinite(num2))
		{
			return false;
		}
		if (action == CombatAction.Attack && settings.AttackMode == AutoAttackMode.Melee)
		{
			return num * num + num2 * num2 <= settings.WeaponRadius * settings.WeaponRadius;
		}
		double num3;
		double num4;
		if (action != CombatAction.Tool)
		{
			(num3, num4) = settings.AttackMode switch
			{
				AutoAttackMode.Fan => (settings.WeaponRadius, settings.EffectiveFanHalfWidth),
				AutoAttackMode.HisuyFan => (settings.HisuyFanRange, settings.HisuyFanHalfWidth),
				_ => (double.NaN, double.NaN),
			};
		}
		else
		{
			double toolRange = settings.ToolRange;
			double toolHalfWidth = settings.ToolHalfWidth;
			num4 = toolHalfWidth;
			num3 = toolRange;
		}
		var (num5, num6) = player.Facing switch
		{
			Direction.Up => (0.0 - num2, Math.Abs(num)),
			Direction.Down => (num2, Math.Abs(num)),
			Direction.Left => (0.0 - num, Math.Abs(num2)),
			Direction.Right => (num, Math.Abs(num2)),
			_ => (double.NaN, double.NaN),
		};
		if (num5 > 0.0 && num5 <= num3)
		{
			return num6 <= num4;
		}
		return false;
	}

	public static string DescribeAttack(CombatAssistSettings settings)
	{
		return settings.AttackMode switch
		{
			AutoAttackMode.Melee => $"Melee radial <= {settings.WeaponRadius} tiles",
			AutoAttackMode.Fan => $"Fan reto <= {settings.WeaponRadius}, meia-largura {settings.EffectiveFanHalfWidth} tiles (tecla AA)",
			AutoAttackMode.HisuyFan => $"Hisuy Fan frontal <= {settings.HisuyFanRange}, meia-largura {settings.HisuyFanHalfWidth} tiles",
			_ => "AA inválido/bloqueado",
		};
	}
}
