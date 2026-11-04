using NinOnlineAutoAssist.Core;

namespace NinOnlineAutoAssist.Core.Geometry;

/// <summary>
/// Options controlling dominant-axis selection, tolerance and anti-oscillation hysteresis.
/// </summary>
public sealed record DirectionCalculatorOptions
{
    /// <summary>
    /// Absolute positional tolerance (in game units). If both |dx| and |dy| are within
    /// this value the calculator returns <see cref="Direction.None"/> (hold position).
    /// Game-unit scale is unknown for Nin Online — tune for your coordinate system. EXPERIMENTAL.
    /// </summary>
    public double PositionTolerance { get; init; } = 4.0;

    /// <summary>
    /// Hysteresis ratio. A direction on the *other* axis is only adopted when its
    /// magnitude exceeds the current axis magnitude multiplied by this ratio.
    /// Ignored when the current facing points away from the target (dot &lt; 0).
    /// Values &lt;= 1.0 disable hysteresis (not recommended; causes oscillation).
    /// </summary>
    public double HysteresisRatio { get; init; } = 1.25;

    public static DirectionCalculatorOptions Default { get; } = new();
}

/// <summary>
/// Computes the desired discrete facing direction from a relative offset.
/// Pure, deterministic and independent of the data source (testable offline).
///
/// Algorithm:
/// 1. If both axes are within tolerance -> Direction.None.
/// 2. Dominant axis = axis with the largest absolute delta.
/// 3. Exact ties: keep the previous facing's axis ONLY if it has positive dot with the
///    offset; otherwise fall back to the horizontal axis (documented, deterministic).
/// 4. Hysteresis: when a previous direction exists AND it does not face away from the
///    target (dot &gt;= 0), the competing axis must exceed the current axis by
///    HysteresisRatio before a switch is allowed. Facing away bypasses hysteresis.
/// </summary>
public static class DirectionCalculator
{
    public static Direction Calculate(double dx, double dy, Direction previous = Direction.None,
        DirectionCalculatorOptions? options = null)
    {
        options ??= DirectionCalculatorOptions.Default;
        if (!double.IsFinite(dx) || !double.IsFinite(dy))
            return previous; // Fail safe: never invent a direction from garbage input.

        double adx = Math.Abs(dx);
        double ady = Math.Abs(dy);
        double tol = Math.Max(0.0, options.PositionTolerance);

        if (adx <= tol && ady <= tol)
            return Direction.None;

        double ratio = Math.Max(1.0, options.HysteresisRatio);

        Direction Horizontal() => dx >= 0 ? Direction.Right : Direction.Left;
        Direction Vertical() => dy >= 0 ? Direction.Down : Direction.Up; // screen Y grows downward

        double facingDot = FacingDot(previous, dx, dy);
        bool facingAway = previous is not Direction.None && facingDot < 0;

        // Hysteresis against current facing (skipped entirely when facing away).
        if (previous is not Direction.None && !facingAway)
        {
            bool prevHoriz = previous.IsHorizontal();
            double prevMag = prevHoriz ? adx : ady;
            double otherMag = prevHoriz ? ady : adx;
            if (otherMag <= prevMag * ratio)
            {
                double held = prevMag;
                return held <= tol ? Direction.None : (prevHoriz ? Horizontal() : Vertical());
            }
        }

        // Dominant axis (with documented tie-break).
        bool xDominant;
        if (adx > ady) xDominant = true;
        else if (ady > adx) xDominant = false;
        else xDominant = TieBreakPrefersHorizontal(previous, dx, dy);

        return xDominant
            ? (adx <= tol ? Direction.None : Horizontal())
            : (ady <= tol ? Direction.None : Vertical());
    }

    /// <summary>
    /// True when the facing direction has a positive dot product with the offset —
    /// i.e. the character is at least partly facing the target. Perpendicular (0) counts as aligned-ish.
    /// </summary>
    public static bool IsFacingTowards(Direction facing, double dx, double dy) =>
        facing is not Direction.None && FacingDot(facing, dx, dy) >= 0;

    private static double FacingDot(Direction facing, double dx, double dy) => facing switch
    {
        Direction.Right => dx,
        Direction.Left => -dx,
        Direction.Down => dy,
        Direction.Up => -dy,
        _ => double.PositiveInfinity // no previous: never treated as "facing away"
    };

    private static bool TieBreakPrefersHorizontal(Direction previous, double dx, double dy)
    {
        if (previous is not Direction.None && FacingDot(previous, dx, dy) > 0)
            return previous.IsHorizontal();
        return true; // documented default on exact tie: horizontal
    }

    /// <summary>Euclidean distance helper used by range checks.</summary>
    public static double Distance(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);
}
