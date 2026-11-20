using System.Collections.Generic;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed class LiveEventDetector
{
	public const long CorrelationWindowMs = 400L;

	private LiveFrame? _previous;

	private (LiveFrame ActiveFrame, long RemovedAt)? _pendingRemoval;

	private long? _recentJumpAt;

	public void Reset()
	{
		_previous = null;
		_pendingRemoval = null;
		_recentJumpAt = null;
	}

	public IReadOnlyList<CombatEvent> Observe(LiveFrame frame, bool manualMovement, double teleportMinDistance = 1.5)
	{
		List<CombatEvent> list = new List<CombatEvent>();
		LiveFrame previous = _previous;
		_previous = frame;
		if ((object)previous != null && (previous.Map != frame.Map || previous.Snapshot.Player.Id != frame.Snapshot.Player.Id || frame.CapturedAtMs <= previous.CapturedAtMs || frame.CapturedAtMs - previous.CapturedAtMs > 500))
		{
			Reset();
			_previous = frame;
			list.Add(CombatEvent.Create(CombatEventKind.ManualInterrupt, frame.CapturedAtMs, EventOrigin.Observed, null, "Map, identity or observation continuity changed"));
			return list;
		}
		long? recentJumpAt = _recentJumpAt;
		if (recentJumpAt.HasValue)
		{
			long valueOrDefault = recentJumpAt.GetValueOrDefault();
			if (frame.CapturedAtMs - valueOrDefault > 400)
			{
				_recentJumpAt = null;
				_pendingRemoval = null;
				list.Add(CombatEvent.Create(CombatEventKind.ManualInterrupt, frame.CapturedAtMs, EventOrigin.Observed, null, "Position jump not corroborated by buff disappearance within correlation window"));
				return list;
			}
		}
		(LiveFrame, long)? pendingRemoval = _pendingRemoval;
		if (pendingRemoval.HasValue)
		{
			(LiveFrame, long) valueOrDefault2 = pendingRemoval.GetValueOrDefault();
			_pendingRemoval = null;
			if (frame.SubstitutionActive)
			{
				return list;
			}
			long num = frame.CapturedAtMs - valueOrDefault2.Item2;
			if (!manualMovement && num <= 400 && IsJump(valueOrDefault2.Item1, frame, teleportMinDistance))
			{
				list.Add(Teleport(frame, "Position update arrived after buff removal; correlated within bounded window"));
				list.Add(Expired(frame));
				return list;
			}
			if (manualMovement || num >= 400)
			{
				list.Add(Expired(frame));
			}
			else
			{
				_pendingRemoval = valueOrDefault2;
			}
			return list;
		}
		if (frame.SubstitutionActive && ((object)previous == null || !previous.SubstitutionActive))
		{
			list.Add(CombatEvent.Create(CombatEventKind.BuffConfirmed, frame.CapturedAtMs, EventOrigin.Observed, null, "Substitution Technique present in active buff array"));
		}
		if ((object)previous != null && previous.SubstitutionActive)
		{
			bool flag = !manualMovement && IsJump(previous, frame, teleportMinDistance);
			if (flag)
			{
				list.Add(Teleport(frame, "Position jump while previously buffed; NOT explicit trigger/attacker identification"));
				_recentJumpAt = frame.CapturedAtMs;
			}
			if (!frame.SubstitutionActive)
			{
				int num2;
				if (!flag)
				{
					recentJumpAt = _recentJumpAt;
					num2 = (recentJumpAt.HasValue ? 1 : 0);
				}
				else
				{
					num2 = 1;
				}
				if (((uint)num2 | (manualMovement ? 1u : 0u)) == 0)
				{
					int? substitutionDuration = previous.SubstitutionDuration;
					if (substitutionDuration.HasValue && substitutionDuration.GetValueOrDefault() > 0)
					{
						_pendingRemoval = (previous, frame.CapturedAtMs);
						goto IL_028a;
					}
				}
				list.Add(Expired(frame));
				_recentJumpAt = null;
			}
		}
		goto IL_028a;
		IL_028a:
		return list;
	}

	private static bool IsJump(LiveFrame before, LiveFrame after, double minimum)
	{
		int? substitutionDuration = before.SubstitutionDuration;
		if (!substitutionDuration.HasValue || substitutionDuration.GetValueOrDefault() <= 0)
		{
			return false;
		}
		PlayerState player = before.Snapshot.Player;
		PlayerState player2 = after.Snapshot.Player;
		double num = DirectionCalculator.Distance(player2.PositionX - player.PositionX, player2.PositionY - player.PositionY);
		if (num >= minimum)
		{
			return num <= 30.0;
		}
		return false;
	}

	private static CombatEvent Teleport(LiveFrame frame, string note)
	{
		return new CombatEvent
		{
			Kind = CombatEventKind.TeleportObserved,
			TimestampMs = frame.CapturedAtMs,
			Origin = EventOrigin.Observed,
			PositionX = frame.Snapshot.Player.PositionX,
			PositionY = frame.Snapshot.Player.PositionY,
			Note = note
		};
	}

	private static CombatEvent Expired(LiveFrame frame)
	{
		return CombatEvent.Create(CombatEventKind.BuffExpired, frame.CapturedAtMs, EventOrigin.Observed, null, "Buff disappeared; alone this is not a substitution trigger");
	}
}
