using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class MapVillageFilterTests
{
    private static MapPlayer Player(int slot, PlayerVillage village, bool local = false, bool selected = false) =>
        new(slot, "Same Name", 1, 0, 0, local, selected) { Village = village };
    private static MapPlayersFrame Frame(params MapPlayer[] players) => new(1, "local", 0, Array.AsReadOnly(players), 0);

    [Theory]
    [InlineData(PlayerVillage.Leaf)] [InlineData(PlayerVillage.Sand)]
    [InlineData(PlayerVillage.Mist)] [InlineData(PlayerVillage.Renegade)]
    public void HidesLocalAndSameVillageButKeepsAllOtherKnownVillages(PlayerVillage own)
    {
        var known = new[] { PlayerVillage.Leaf, PlayerVillage.Sand, PlayerVillage.Mist, PlayerVillage.Renegade };
        var records = known.Select((v, i) => Player(i + 2, v)).Prepend(Player(1, own, local: true)).ToArray();
        var frame = Frame(records);
        var visible = frame.ForDisplay(true);
        Assert.Equal(3, visible.Count);
        Assert.DoesNotContain(visible, p => p.IsLocal || p.Village == own);
        Assert.Equal(known.Where(v => v != own), visible.Select(p => p.Village));
        Assert.Equal(5, frame.Players.Count); // Source is unchanged.
        Assert.Same(frame.Players, frame.ForDisplay(false));
    }

    [Fact]
    public void UnknownOtherVillageIsNotPresentedAsKnownDifferentVillage()
    {
        var frame = Frame(Player(1, PlayerVillage.Leaf, true), Player(2, PlayerVillage.Unknown),
            Player(3, (PlayerVillage)999), Player(4, PlayerVillage.Mist, selected: true));
        var visible = Assert.Single(frame.ForDisplay(true));
        Assert.Equal(4, visible.Slot); Assert.True(visible.IsSelected);
        Assert.Equal(4, frame.ForDisplay(false).Count);
    }

    [Fact]
    public void UnknownMissingOrAmbiguousLocalVillageDoesNotGuessEnemies()
    {
        foreach (var frame in new[] {
            Frame(Player(1, PlayerVillage.Unknown, true), Player(2, PlayerVillage.Mist)),
            Frame(Player(2, PlayerVillage.Mist)),
            Frame(Player(1, PlayerVillage.Leaf, true), Player(2, PlayerVillage.Mist, true)),
            Frame(Player(1, (PlayerVillage)999, true), Player(2, PlayerVillage.Mist)) })
        {
            Assert.Equal(PlayerVillage.Unknown, frame.LocalVillage);
            Assert.Empty(frame.ForDisplay(true)); Assert.NotEmpty(frame.ForDisplay(false));
        }
    }

    [Fact]
    public void NewFrameReevaluatesChangedVillageAndDoesNotUseNameOrOldLocalVillage()
    {
        var leaf = Frame(Player(1, PlayerVillage.Leaf, true), Player(2, PlayerVillage.Leaf), Player(3, PlayerVillage.Mist));
        Assert.Equal(3, Assert.Single(leaf.ForDisplay(true)).Slot);
        var mist = leaf with { Players = new[] { leaf.Players[0] with { Village = PlayerVillage.Mist }, leaf.Players[1], leaf.Players[2] } };
        Assert.Equal(2, Assert.Single(mist.ForDisplay(true)).Slot);
        Assert.Equal(3, Assert.Single(leaf.ForDisplay(true)).Slot);
    }
}
