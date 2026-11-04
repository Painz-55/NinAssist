using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class StaminaSettingsTests
{
    [Theory]
    [InlineData(-1, 50)]
    [InlineData(20, 101)]
    [InlineData(50, 50)]
    [InlineData(70, 30)]
    public void InvalidOrOverlappingThresholdsCannotBeApplied(int off, int on)
    {
        var settings = new CombatAssistSettings { StaminaDisableBelowPercent = off, StaminaEnableAbovePercent = on };
        Assert.Throws<ArgumentException>(settings.Validate);
        Assert.Throws<ArgumentException>(() => new AutoStaminaEngine(new SimulatedClock(0), _ => ActionResult.Ok(), off, on));
    }

    [Fact]
    public void CustomThresholdsPersistAndOldConfigsKeepTheDefaults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ninautoassist-stamina-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{}");
            var old = ConfigLoader.Load(path);
            Assert.Equal(20, old.CombatAssist.StaminaDisableBelowPercent);
            Assert.Equal(50, old.CombatAssist.StaminaEnableAbovePercent);
            ConfigLoader.Save(path, old with { CombatAssist = old.CombatAssist with
                { StaminaDisableBelowPercent = 30, StaminaEnableAbovePercent = 75 } });
            var updated = ConfigLoader.Load(path);
            Assert.Equal(30, updated.CombatAssist.StaminaDisableBelowPercent);
            Assert.Equal(75, updated.CombatAssist.StaminaEnableAbovePercent);
        }
        finally { File.Delete(path); }
    }
}
