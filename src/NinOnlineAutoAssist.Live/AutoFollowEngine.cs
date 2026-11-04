using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Live;

public sealed record FollowStep(Direction Direction, GridPoint Destination, Direction SecondaryDirection = Direction.None)
{
    public GridPoint? Offset
    {
        get
        {
            static GridPoint? Vector(Direction d) => d switch
            {
                Direction.Up => new(0, -1), Direction.Down => new(0, 1),
                Direction.Left => new(-1, 0), Direction.Right => new(1, 0), _ => null
            };
            if (Vector(Direction) is not { } primary) return null;
            if (SecondaryDirection == Direction.None) return primary;
            if (Vector(SecondaryDirection) is not { } secondary || primary.X * secondary.X + primary.Y * secondary.Y != 0) return null;
            return new(primary.X + secondary.X, primary.Y + secondary.Y);
        }
    }
    public string MovementText => SecondaryDirection == Direction.None ? Direction.ToString() : $"{Direction}+{SecondaryDirection}";
    public FollowStep From(GridPoint start) => Offset is { } offset
        ? this with { Destination = new(start.X + offset.X, start.Y + offset.Y) } : this;
    public static FollowStep Between(GridPoint start, GridPoint end)
    {
        var dx = end.X - start.X; var dy = end.Y - start.Y;
        var vertical = dy < 0 ? Direction.Up : Direction.Down;
        var horizontal = dx < 0 ? Direction.Left : Direction.Right;
        return dy == 0 ? new(horizontal, end) : new(vertical, end, dx == 0 ? Direction.None : horizontal);
    }
}

public static class FollowPathPlanner
{
    public static FollowStep? Next(LiveFrame frame, NavigationFrame grid, double stopDistance, FollowStep? previous = null)
    {
        if (!grid.DynamicHazardsKnown || grid.Map != frame.Map || !double.IsFinite(stopDistance) || stopDistance < 0.5) return null;
        if (frame.Snapshot.Target is not { IsAlive: true, IsTargeted: true } target) return null;
        var player = frame.Snapshot.Player;
        if (!double.IsFinite(player.PositionX) || !double.IsFinite(player.PositionY) ||
            !double.IsFinite(target.PositionX) || !double.IsFinite(target.PositionY)) return null;
        var start = new GridPoint((int)Math.Floor(player.PositionX), (int)Math.Floor(player.PositionY));
        if (grid.Cell(start.X, start.Y) == NavigationCell.Unknown) return null;
        var actualDx = target.PositionX - player.PositionX;
        var actualDy = target.PositionY - player.PositionY;
        var distance = Math.Sqrt(actualDx * actualDx + actualDy * actualDy);
        if (distance <= stopDistance) return null;
        var fraction = (distance - stopDistance) / distance;
        var direct = FollowSteering.Heading(actualDx, actualDy, start, previous);
        if (grid.SafeStep(start, direct) && FollowSteering.ClearSegment(grid, player.PositionX, player.PositionY,
            player.PositionX + actualDx * fraction, player.PositionY + actualDy * fraction)) return direct;
        var targetPoint = new GridPoint((int)Math.Floor(target.PositionX), (int)Math.Floor(target.PositionY));
        // A*: cardinal cost 1, diagonal sqrt(2), with no corner cutting.
        var queue = new PriorityQueue<(GridPoint Point, double Cost), double>();
        var parent = new Dictionary<GridPoint, GridPoint>();
        var costs = new Dictionary<GridPoint, double> { [start] = 0 };
        double Heuristic(GridPoint p) => Math.Max(0, Math.Sqrt(Math.Pow(p.X - targetPoint.X, 2) + Math.Pow(p.Y - targetPoint.Y, 2)) - stopDistance);
        queue.Enqueue((start, 0), Heuristic(start)); parent[start] = start;
        GridPoint? reached = null;
        var offsets = new[] { new GridPoint(0, -1), new GridPoint(0, 1), new GridPoint(-1, 0), new GridPoint(1, 0),
            new GridPoint(-1, -1), new GridPoint(1, -1), new GridPoint(-1, 1), new GridPoint(1, 1) };
        while (queue.Count > 0 && parent.Count <= 4096)
        {
            var entry = queue.Dequeue();
            var current = entry.Point;
            if (entry.Cost > costs[current]) continue;
            var dx = current.X - targetPoint.X;
            var dy = current.Y - targetPoint.Y;
            if (current != start && dx * dx + dy * dy <= stopDistance * stopDistance && grid.Safe(current))
            { reached = current; break; }
            foreach (var offset in offsets)
            {
                var next = new GridPoint(current.X + offset.X, current.Y + offset.Y);
                if (!grid.SafeStep(current, FollowStep.Between(current, next))) continue;
                var cost = entry.Cost + (offset.X != 0 && offset.Y != 0 ? Math.Sqrt(2) : 1);
                if (costs.TryGetValue(next, out var oldCost) && cost >= oldCost) continue;
                costs[next] = cost; parent[next] = current;
                queue.Enqueue((next, cost), cost + Heuristic(next));
            }
        }
        if (reached is not { } end) return null;
        while (parent[end] != start) end = parent[end];
        return FollowStep.Between(start, end);
    }
}

public sealed class AutoFollowEngine
{
    private readonly IMonotonicClock _clock;
    private readonly CombatAssistSettings _settings;
    private readonly Func<FollowStep, ActionResult> _move;
    private readonly Action _release;
    private bool _maintaining;
    private long? _stuckSince;
    private (double X, double Y)? _lastPosition;
    private string? _target;
    private bool _stuck;
    private FollowStep? _previousStep;
    public string Status { get; private set; } = "Follow OFF";

    public AutoFollowEngine(IMonotonicClock clock, CombatAssistSettings settings, Func<FollowStep, ActionResult> move, Action? release = null)
    { settings.Validate(); _clock = clock; _settings = settings; _move = move; _release = release ?? (() => { }); }
    public void Reset()
    {
        _release();
        _stuckSince = null; _lastPosition = null; _target = null; _stuck = false;
        _previousStep = null;
        Status = "Follow: segure a tecla configurada";
    }
    public void Update(LiveFrame? frame, NavigationFrame? grid, bool allowed, bool higherPriorityBusy)
    {
        _maintaining = false;
        try { UpdateCore(frame, grid, allowed, higherPriorityBusy); }
        finally { if (!_maintaining) { _previousStep = null; _release(); } }
    }
    private void UpdateCore(LiveFrame? frame, NavigationFrame? grid, bool allowed, bool higherPriorityBusy)
    {
        if (!allowed) { Reset(); return; }
        var now = _clock.NowMs;
        if (frame is null || grid is null || now < frame.CapturedAtMs || now - frame.CapturedAtMs > 500 ||
            frame.ReadDurationMs is < 0 or > 500 ||
            now < grid.CapturedAtMs || now - grid.CapturedAtMs > 500 || grid.Map != frame.Map || !grid.DynamicHazardsKnown)
        { Status = "Follow bloqueado: terreno/perigos ausentes, desconhecidos ou antigos"; return; }
        var target = frame.Snapshot.Target;
        if (!frame.Snapshot.Player.IsAlive || target is not { IsAlive: true, IsTargeted: true })
        { Status = "Follow: sem alvo/jogador válido"; return; }
        if (_target != target.Id) { Reset(); _target = target.Id; }
        if (higherPriorityBusy) { _stuckSince = null; Status = "Follow: aguardando prioridade do SubPunish"; return; }
        var p = frame.Snapshot.Player;
        var distance = Math.Sqrt(Math.Pow(target.PositionX - p.PositionX, 2) + Math.Pow(target.PositionY - p.PositionY, 2));
        if (distance <= _settings.FollowStopDistance)
        { _stuckSince = null; Status = "Follow: distância de parada alcançada"; return; }
        var step = FollowPathPlanner.Next(frame, grid, _settings.FollowStopDistance, _previousStep);
        if (step is null)
        {
            // A temporary projectile/path blockage is not evidence that a HELD movement command is stuck.
            _stuckSince = null; _lastPosition = null;
            Status = "Follow: sem caminho conhecido seguro; aguardando rota livre"; return;
        }
        if (_stuck) { Status = "Follow parado: sem progresso; solte e pressione a tecla novamente"; return; }
        if (_lastPosition is { } previous && Math.Abs(p.PositionX - previous.X) + Math.Abs(p.PositionY - previous.Y) < 0.05)
        {
            _stuckSince ??= now;
            if (_stuckSince is { } since && now - since > 1500)
            { _stuck = true; Status = "Follow parado: obstáculo/sem progresso"; return; }
        }
        else { _lastPosition = (p.PositionX, p.PositionY); _stuckSince = now; }
        // Replan continuously; the adapter renews the same held direction without releasing it.
        var result = _move(step);
        _maintaining = result.Accepted;
        _previousStep = result.Accepted ? step : null;
        if (!result.Accepted) { _stuckSince = null; _lastPosition = null; }
        Status = result.Accepted ? $"Follow: {step.MovementText}; evitando tiles/perigos observados" : $"Follow: {result.Reason}";
    }
}
