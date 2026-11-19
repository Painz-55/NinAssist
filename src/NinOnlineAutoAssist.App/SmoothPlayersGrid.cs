namespace NinOnlineAutoAssist.App;

/// <summary>Buffers frequent local-map cell updates without replacing the whole table.</summary>
internal sealed class SmoothPlayersGrid : DataGridView
{
    public SmoothPlayersGrid() => DoubleBuffered = true;
}
