namespace NinOnlineAutoAssist.Core;

/// <summary>
/// Discrete facing directions supported by Nin Online (4-way isometric-style turning).
/// </summary>
public enum Direction
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4
}

public static class DirectionExtensions
{
    public static bool IsHorizontal(this Direction d) => d is Direction.Left or Direction.Right;
    public static bool IsVertical(this Direction d) => d is Direction.Up or Direction.Down;

    public static Direction Opposite(this Direction d) => d switch
    {
        Direction.Up => Direction.Down,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        Direction.Right => Direction.Left,
        _ => Direction.None
    };
}
