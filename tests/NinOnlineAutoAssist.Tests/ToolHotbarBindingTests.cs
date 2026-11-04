using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Tests;

public sealed class ToolHotbarBindingTests
{
    private static Dictionary<string, string[]> Bindings() => new()
    {
        ["Attack"] = new[] { "A", "E" },
        ["ThrowWeapon"] = new[] { "Z", "F" },
        ["Guard"] = new[] { "Z" },
        ["HotbarF7"] = new[] { "S" },
        ["Hotbar1"] = new[] { "Q" }
    };

    [Fact]
    public void ActualCustomSBindingUsesHotbarF7NotThrowWeapon()
    {
        Assert.Equal("S", new CombatAssistSettings().ToolKey);
        Assert.Equal("HotbarF7", CombatBindingValidator.ResolveToolSlot(Bindings(), "S"));
        Assert.Equal("HotbarF7", CombatBindingValidator.ResolveToolSlot(Bindings(), "s"));
    }

    [Fact]
    public void AThrowWeaponKeyIsNeverAcceptedAsAShurikenTool()
    {
        var error = Assert.Throws<ArgumentException>(() => CombatBindingValidator.ResolveToolSlot(Bindings(), "Z"));
        Assert.Contains("arremesso da arma", error.Message);
    }

    [Fact]
    public void SharingTheSameKeyWithThrowWeaponIsAlsoRejected()
    {
        var bindings = Bindings();
        bindings["ThrowWeapon"] = new[] { "Z", "S" };
        Assert.Throws<ArgumentException>(() => CombatBindingValidator.ResolveToolSlot(bindings, "S"));
    }

    [Fact]
    public void AHotbarKeyDoesNotRequireAnyThrowWeaponBinding()
    {
        var bindings = Bindings(); bindings.Remove("ThrowWeapon");
        Assert.Equal("HotbarF7", CombatBindingValidator.ResolveToolSlot(bindings, "S"));
    }

    [Fact]
    public void MultipleSlotsOnTheSameKeyAreAmbiguousAndRejected()
    {
        var bindings = Bindings(); bindings["Hotbar2"] = new[] { "S" };
        Assert.Throws<ArgumentException>(() => CombatBindingValidator.ResolveToolSlot(bindings, "S"));
    }

    [Theory]
    [InlineData("HotbarCycle")]
    [InlineData("HotbarCycleBack")]
    [InlineData("Inventory")]
    public void NonSlotActionsDoNotQualifyAsToolShortcuts(string action)
    {
        var bindings = new Dictionary<string, string[]> { [action] = new[] { "S" } };
        Assert.Throws<ArgumentException>(() => CombatBindingValidator.ResolveToolSlot(bindings, "S"));
    }

    [Fact]
    public void NumericHotbarBindingsAreNormalized()
    {
        var bindings = new Dictionary<string, string[]> { ["HotbarF2"] = new[] { "Num2" } };
        Assert.Equal("HotbarF2", CombatBindingValidator.ResolveToolSlot(bindings, "2"));
    }

    [Theory]
    [InlineData("Hotbar0", true)]
    [InlineData("Hotbar9", true)]
    [InlineData("HotbarF1", true)]
    [InlineData("HotbarF7", true)]
    [InlineData("HotbarF10", true)]
    [InlineData("Hotbar10", false)]
    [InlineData("HotbarF0", false)]
    [InlineData("HotbarF11", false)]
    [InlineData("HotbarF01", false)]
    [InlineData("HotbarCycle", false)]
    [InlineData("Attack", false)]
    public void OnlyRealHotbarSlotNamesAreAccepted(string action, bool expected) =>
        Assert.Equal(expected, CombatBindingValidator.IsHotbarSlot(action));
}
