using System.Diagnostics;
using System.Text.Json;
using NinOnlineAutoAssist.Diagnostics;

namespace NinOnlineAutoAssist.Tests;

public sealed class ResourceInspectionTests
{
    [Fact]
    public void RefusesUnrelatedProcessBeforeAttaching()
    {
        using var current = Process.GetCurrentProcess();
        var error = Assert.Throws<InvalidOperationException>(() => LiveInspection.Inspect(current.Id));
        Assert.Contains("Expected NinOnline.exe", error.Message);
    }

    [Fact]
    public void ObservedSpellPreservesFloatCastTimeAndLevel()
    {
        var spell = new ObservedSpell(4, 2, "Example", 10, 0.75f, 15, 0);
        var copy = JsonSerializer.Deserialize<ObservedSpell>(JsonSerializer.Serialize(spell));
        Assert.Equal(spell, copy);
        Assert.Equal(0.75f, copy!.CastTime);
    }

    [Fact]
    public void ReportPreservesIncompleteReadAndUnknownCombatState()
    {
        var report = new ResourceInspection(123, DateTimeOffset.UtcNow, "Observed", "Passive", 100,
            20, true, 1, Array.Empty<ObservedType>(), Array.Empty<ObservedSpell>(), null);
        var copy = JsonSerializer.Deserialize<ResourceInspection>(JsonSerializer.Serialize(report));
        Assert.True(copy!.Truncated);
        Assert.Equal(1, copy.FailedSpellReads);
        Assert.Empty(copy.Spells);
        Assert.Null(copy.SubstitutionSample);
    }
}
