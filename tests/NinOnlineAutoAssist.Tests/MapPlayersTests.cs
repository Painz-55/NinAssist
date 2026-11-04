using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class MapPlayersTests
{
    private static LiveFrame Live(long at = 100, int map = 51, string id = "1:Local") => new(
        new GameSnapshot { Player = new PlayerState { Id = id } }, at, map, "Local", null, false, null, 0);

    [Fact]
    public void IncludesAllNamedMapRecordsWithoutVisibilityAppearanceOrPositionFilters()
    {
        var rows = MapPlayersFrame.Filter(new[]
        {
            new MapPlayer(2, "HiddenRecord", 51, null, null, false, false),
            new MapPlayer(3, "TransformedRecord", 51, 3, 4, false, true),
            new MapPlayer(1, "Local", 51, 1, 2, true, false),
            new MapPlayer(4, "OtherMap", 52, 1, 2, false, false),
            new MapPlayer(5, "", 51, 1, 2, false, false),
            new MapPlayer(0, "InvalidSlot", 51, 1, 2, false, false)
        }, 51);
        Assert.Equal(new[] { "Local", "HiddenRecord", "TransformedRecord" }, rows.Select(p => p.Name));
        Assert.Null(rows[1].X);
        // These are inclusion tests, not proof that the live hidden/transform state is mapped.
    }

    [Theory]
    [InlineData(100, 51, "1:Local", true)]
    [InlineData(1600, 51, "1:Local", true)]
    [InlineData(1601, 51, "1:Local", false)]
    [InlineData(99, 51, "1:Local", false)]
    [InlineData(100, 52, "1:Local", false)]
    [InlineData(100, 51, "2:Other", false)]
    public void StaleWrongMapOrCharacterDataIsNeverShownAsCurrent(long now, int map, string id, bool expected)
    {
        var list = new MapPlayersFrame(51, "1:Local", 100, Array.Empty<MapPlayer>(), 0);
        Assert.Equal(expected, list.IsCurrent(Live(now, map, id), now));
    }

    [Fact]
    public void DisconnectionAndStaleMainFrameInvalidateEvenAFreshList()
    {
        var list = new MapPlayersFrame(51, "1:Local", 100, Array.Empty<MapPlayer>(), 0);
        Assert.False(list.IsCurrent(null, 100));
        Assert.False(list.IsCurrent(Live(100), 601));
    }
}
