using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Tests;

public sealed class RunBindingValidatorTests
{
    [Theory]
    [InlineData("LControl", "ControlModifier", 0xA2)]
    [InlineData("RControl", "ControlModifier", 0xA3)]
    [InlineData("LShift", "ShiftModifier", 0xA0)]
    [InlineData("RShift", "ShiftModifier", 0xA1)]
    public void NativeModifierRegistrationIsNotASecondAction(string key, string modifier, ushort expected)
    {
        var bindings = new Dictionary<string, string[]> { ["Run"] = new[] { key }, [modifier] = new[] { key },
            ["Attack"] = new[] { "A" }, ["HotbarF7"] = new[] { "S" } };
        Assert.Equal(expected, RunBindingValidator.Resolve(bindings, 0));
    }

    [Theory]
    [InlineData("Attack")]
    [InlineData("Hotbar0")]
    [InlineData("Guard")]
    [InlineData("UnknownAction")]
    [InlineData("ShiftModifier")]
    public void OnlyMatchingModifierIsExemptFromConflicts(string action)
    {
        var bindings = new Dictionary<string, string[]> { ["Run"] = new[] { "LControl" }, [action] = new[] { "LControl" } };
        Assert.Throws<ArgumentException>(() => RunBindingValidator.Resolve(bindings, 0));
    }

    [Fact]
    public void FollowRunKeyCollisionRemainsBlocked()
    {
        var bindings = new Dictionary<string, string[]> { ["Run"] = new[] { "LControl" } };
        Assert.Throws<ArgumentException>(() => RunBindingValidator.Resolve(bindings, 0xA2));
    }

    [Theory]
    [InlineData("{\"Options\":{\"HoldToRun\":\"False\"}}", true)]
    [InlineData("{\"Options\":{\"HoldToRun\":\"false\",\"Sound\":99}}", true)]
    [InlineData("{\"Options\":{\"HoldToRun\":false}}", true)]
    [InlineData("{\"Options\":{\"HoldToRun\":\"True\"}}", false)]
    [InlineData("{\"Options\":{\"HoldToRun\":true}}", false)]
    [InlineData("{\"Options\":{\"HoldToRun\":\"unknown\"}}", false)]
    [InlineData("{\"Options\":{\"HoldToRun\":null}}", false)]
    public void OptionsAreValidatedByMeaningNotWholeFileTimestamp(string json, bool expected) =>
        Assert.Equal(expected, RunBindingValidator.IsToggleMode(json));
}
