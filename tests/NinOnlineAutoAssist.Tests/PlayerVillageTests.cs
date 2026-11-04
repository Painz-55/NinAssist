using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class PlayerVillageTests
{
    [Theory]
    [InlineData(1, PlayerVillage.Leaf, "Leaf", unchecked((int)0xFF98C379))]
    [InlineData(2, PlayerVillage.Sand, "Sand", unchecked((int)0xFFE5C07B))]
    [InlineData(3, PlayerVillage.Mist, "Mist", unchecked((int)0xFF61AFEF))]
    [InlineData(11, PlayerVillage.Renegade, "Renegado", unchecked((int)0xFFE06C75))]
    [InlineData(12, PlayerVillage.Renegade, "Renegado", unchecked((int)0xFFE06C75))]
    [InlineData(13, PlayerVillage.Renegade, "Renegado", unchecked((int)0xFFE06C75))]
    [InlineData(20, PlayerVillage.Renegade, "Renegado", unchecked((int)0xFFE06C75))]
    public void KnownVillageCodesHaveRequestedLabelsAndColors(byte code, PlayerVillage expected, string label, int color)
    {
        var village = PlayerVillages.FromCode(code);
        Assert.Equal(expected, village); Assert.Equal(label, PlayerVillages.Label(village));
        Assert.Equal(color, PlayerVillages.ColorArgb(village));
    }

    [Theory]
    [InlineData(null)] [InlineData(0)] [InlineData(4)] [InlineData(9)] [InlineData(10)] [InlineData(21)] [InlineData(255)]
    public void MissingAndUnmappedCodesNeverBecomeRenegadesOrReceiveVillageColors(int? raw)
    {
        var village = PlayerVillages.FromCode(raw is { } code ? (byte)code : null);
        Assert.Equal(PlayerVillage.Unknown, village); Assert.Equal("Desconhecida", PlayerVillages.Label(village));
        Assert.Null(PlayerVillages.ColorArgb(village));
    }

    [Fact]
    public void UnmappedProfilesAndOldRecordsDefaultToUnknown()
    {
        Assert.Null(GameMemoryProfile.Standalone.Village);
        Assert.Equal(PlayerVillage.Unknown, new MapPlayer(1, "Dopamine", 1, 0, 0, false, false).Village);
        Assert.Equal("Desconhecida", PlayerVillages.Label((PlayerVillage)999));
        Assert.Null(PlayerVillages.ColorArgb((PlayerVillage)999));
    }

    [Fact]
    public void NameNeverDeterminesVillageAndDuplicateNamesKeepSeparateSlots()
    {
        var players = MapPlayersFrame.Filter(new[]
        {
            new MapPlayer(4, "Same Name", 1, 0, 0, false, false) { Village = PlayerVillage.Leaf },
            new MapPlayer(7, "Same Name", 1, 1, 0, false, true) { Village = PlayerVillage.Mist },
            new MapPlayer(9, "Renegado", 1, 0, 0, false, false)
        }, 1);
        Assert.Equal(3, players.Count);
        Assert.Equal(PlayerVillage.Leaf, players.Single(p => p.Slot == 4).Village);
        Assert.Equal(PlayerVillage.Mist, players.Single(p => p.Slot == 7).Village);
        Assert.Equal(PlayerVillage.Unknown, players.Single(p => p.Slot == 9).Village);
    }
}
