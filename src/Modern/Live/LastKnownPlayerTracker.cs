using System;
using System.Collections.Generic;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public sealed class LastKnownPlayerTracker
{
	private sealed record Entry(string Name, double X, double Y, PlayerVillage Village, bool Selected, long SeenAtMs, long MovedAtMs);

	private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

	private int _map;

	private string? _localId;

	private long _lastCapture = -1L;

	private const long RetentionMs = 7000L;

	public void Reset()
	{
		_entries.Clear();
		_map = 0;
		_localId = null;
		_lastCapture = -1L;
	}

	public IReadOnlyList<NearbyPlayerMarkers.Marker> Update(MapPlayersFrame? list, LiveFrame? frame, long nowMs, bool hideOwnVillage = false, IReadOnlyCollection<PlayerVillage>? hiddenVillages = null, int maxCensusAgeMs = 300)
	{
		if ((object)frame == null || (_localId != null && (_localId != frame.Snapshot.Player.Id || _map != frame.Map)))
		{
			Reset();
			return Array.Empty<NearbyPlayerMarkers.Marker>();
		}
		if ((object)list == null || !list.IsCurrent(frame, nowMs) || nowMs - list.CapturedAtMs > maxCensusAgeMs || (object)list.RenderView == null)
		{
			return Array.Empty<NearbyPlayerMarkers.Marker>();
		}
		if (_map != list.Map || _localId != list.LocalId || _lastCapture > list.CapturedAtMs)
		{
			Reset();
			_map = list.Map;
			_localId = list.LocalId;
		}
		if (_lastCapture != list.CapturedAtMs)
		{
			_lastCapture = list.CapturedAtMs;
			foreach (MapPlayer item in list.Players.Where((MapPlayer p) => !p.IsLocal))
			{
				double? x = item.X;
				if (!x.HasValue)
				{
					continue;
				}
				double valueOrDefault = x.GetValueOrDefault();
				x = item.Y;
				if (x.HasValue)
				{
					double valueOrDefault2 = x.GetValueOrDefault();
					if (double.IsFinite(valueOrDefault) && double.IsFinite(valueOrDefault2))
					{
						Entry valueOrDefault3 = _entries.GetValueOrDefault(item.Slot);
						long movedAtMs = ((valueOrDefault3?.Name == item.Name && Math.Abs(valueOrDefault3.X - valueOrDefault) < 0.05 && Math.Abs(valueOrDefault3.Y - valueOrDefault2) < 0.05) ? valueOrDefault3.MovedAtMs : list.CapturedAtMs);
						_entries[item.Slot] = new Entry(item.Name, valueOrDefault, valueOrDefault2, item.Village, item.IsSelected, list.CapturedAtMs, movedAtMs);
					}
				}
			}
		}
		KeyValuePair<int, Entry>[] array = _entries.ToArray();
		for (int num = 0; num < array.Length; num++)
		{
			KeyValuePair<int, Entry> keyValuePair = array[num];
			int key = keyValuePair.Key;
			Entry entry2 = keyValuePair.Value;
			if (nowMs < entry2.SeenAtMs || nowMs - entry2.SeenAtMs > 7000)
			{
				_entries.Remove(key);
			}
		}
		double xLocal = frame.Snapshot.Player.PositionX;
		double yLocal = frame.Snapshot.Player.PositionY;
		if (!double.IsFinite(xLocal) || !double.IsFinite(yLocal))
		{
			Reset();
			return Array.Empty<NearbyPlayerMarkers.Marker>();
		}
		IReadOnlyCollection<PlayerVillage> hidden = (IReadOnlyCollection<PlayerVillage>)(((object)hiddenVillages) ?? ((object)Array.Empty<PlayerVillage>()));
		return Array.AsReadOnly((from e in _entries
			where MapPlayersFrame.VillageVisible(e.Value.Village, isLocal: false, list.LocalVillage, hideOwnVillage, hidden)
			select new NearbyPlayerMarkers.Marker(e.Key, e.Value.Name, e.Value.X - xLocal, e.Value.Y - yLocal, e.Value.Selected, nowMs - e.Value.SeenAtMs, nowMs - e.Value.MovedAtMs, e.Value.SeenAtMs == list.CapturedAtMs) into m
			orderby (!m.CurrentlyReported) ? 1 : 0, m.ObservationAgeMs
			select m).Take(256).ToArray());
	}
}
