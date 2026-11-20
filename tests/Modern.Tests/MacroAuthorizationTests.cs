using NinOnlineAutoAssist.App;

namespace Modern.Tests;

public class MacroAuthorizationTests
{
    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void SubPunishOnIsTheOnlyAuthorization(bool on, bool permitted, bool validated, bool expected) =>
        Assert.Equal(expected, MacroAuthorization.SubPunish(on, permitted, validated));

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void CombatOnRequiresItsOwnPermission(bool on, bool attack, bool tool, bool expected) =>
        Assert.Equal(expected, MacroAuthorization.Combat(on, attack, tool));

    [Fact]
    public void OffAndPermissionLossRevokeUntilExplicitlyEnabledAgain()
    {
        Assert.True(MacroAuthorization.SubPunish(true, true, true));
        Assert.False(MacroAuthorization.SubPunish(false, true, true));
        Assert.False(MacroAuthorization.SubPunish(true, false, true));
        Assert.True(MacroAuthorization.Combat(true, true, false));
        Assert.False(MacroAuthorization.Combat(false, true, false));
        Assert.False(MacroAuthorization.Combat(true, false, false));
    }
}
