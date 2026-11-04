using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Live;

/// <summary>Owns only successful key-downs. Same-direction renewals never pulse key-up/down.</summary>
public sealed class HeldDirectionController : IDisposable
{
    private readonly Func<Direction, bool> _isDown, _down, _up;
    private readonly HashSet<Direction> _owned = new();
    public Direction Held => _owned.FirstOrDefault();
    public bool Owns(Direction direction) => _owned.Contains(direction);
    public bool Faulted { get; private set; }
    public HeldDirectionController(Func<Direction, bool> isDown, Func<Direction, bool> down, Func<Direction, bool> up)
    { _isDown = isDown; _down = down; _up = up; }
    public bool Set(Direction direction, bool allowed, Direction secondary = Direction.None)
    {
        var step = new FollowStep(direction, default, secondary);
        if (!allowed || step.Offset is null || Faulted) { Release(); return false; }
        var desired = secondary == Direction.None ? new[] { direction } : new[] { direction, secondary };
        try
        {
            if (desired.Any(d => !_owned.Contains(d) && _isDown(d))) { Release(); return false; }
            // Preserve shared components (Up+Right -> Right never pulses Right).
            foreach (var old in _owned.Where(d => !desired.Contains(d)).ToArray())
            {
                if (_up(old)) _owned.Remove(old);
                else { Faulted = true; Release(); return false; }
            }
            foreach (var next in desired.Where(d => !_owned.Contains(d)))
            {
                if (!_down(next)) { Release(); return false; }
                _owned.Add(next);
            }
            return true;
        }
        catch { Release(); throw; }
    }
    public void Release()
    {
        // Retain ownership on failure so disposal/safety service can retry key-up.
        foreach (var direction in _owned.ToArray())
        {
            if (_up(direction)) _owned.Remove(direction);
            else Faulted = true;
        }
    }
    public void Dispose() => Release();
}
