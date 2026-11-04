using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class LocalCharacterIdentityTests
{
    [Fact]
    public void AutoDetectionPinsOnlyAfterTwoMatchingValidObservations()
    {
        var identity = new LocalCharacterIdentity();
        Assert.False(identity.ConfirmStableObservation("Player One", "Player Two"));
        Assert.Null(identity.Name);
        Assert.True(identity.ConfirmStableObservation("Player One", "Player One"));
        Assert.Equal("Player One", identity.Name);
        Assert.True(identity.ConfirmStableObservation("Player One", "Player One"));
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" Player")]
    [InlineData("Player ")]
    [InlineData("Player\nOther")]
    [InlineData("Player\0")]
    public void InvalidReadNeverPinsIdentity(string? value)
    {
        var identity = new LocalCharacterIdentity();
        Assert.False(identity.ConfirmStableObservation(value, value));
        Assert.Null(identity.Name);
    }
    [Fact]
    public void CharacterChangeRequiresNewConnectionAndCannotReplacePinnedIdentity()
    {
        var identity = new LocalCharacterIdentity();
        Assert.True(identity.ConfirmStableObservation("Player One", "Player One"));
        Assert.False(identity.ConfirmStableObservation("Player Two", "Player Two"));
        Assert.Equal("Player One", identity.Name);
        Assert.True(identity.IsInvalidated);
        Assert.False(identity.ConfirmStableObservation("Player One", "Player One"));
    }
    [Fact]
    public void ManualConfirmationStillRequiresExactName()
    {
        var identity = new LocalCharacterIdentity("Player One");
        Assert.True(identity.ConfirmStableObservation("Player One", "Player One"));
        Assert.False(identity.ConfirmStableObservation("player one", "player one"));
        Assert.True(identity.IsInvalidated);
    }
    [Fact]
    public void InconsistentReadDoesNotInvalidateAnOtherwisePinnedIdentity()
    {
        var identity = new LocalCharacterIdentity("Player One");
        Assert.False(identity.ConfirmStableObservation("Player Two", "Player Three"));
        Assert.False(identity.IsInvalidated);
        Assert.True(identity.ConfirmStableObservation("Player One", "Player One"));
    }
    [Fact]
    public void ExcessivelyLongNameCannotBeDetectedOrEnteredManually()
    {
        var longName = new string('A', 65);
        Assert.False(new LocalCharacterIdentity().ConfirmStableObservation(longName, longName));
        Assert.Throws<ArgumentException>(() => new LocalCharacterIdentity(longName));
    }
}
