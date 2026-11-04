using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class SelectedTargetSchemaTests
{
    [Theory]
    [InlineData(1, 10, 10)]
    [InlineData(1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(2, 10, 0)]
    [InlineData(3, 10, 0)]
    public void OnlyValidatedPlayerKindCanBecomeAPlayerTarget(int kind, int index, int expected)
    {
        var schema = new SelectedTargetMemoryFields("object", "kind", "index", 1);
        Assert.Equal(expected, schema.PlayerIndex(kind, index));
    }
}
