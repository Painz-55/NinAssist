using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Simulation;

namespace NinOnlineAutoAssist.Tests;

public sealed class RestrictedSubPunishTests
{
    private static LiveFrame Frame(long at, params string[] names) => new(
        new GameSnapshot { Player = new PlayerState { Id = "local", IsAlive = true } },
        at, 190, "local", null, names.Length > 0, 5, 0)
    { ObservedActiveBuffs = names.Select(name => new ObservedBuff(name, 5)).ToArray() };

    private static (EarlyBuffPunishEngine Engine, SimulatedClock Clock, SimulatedActionExecutor Inputs) Rig()
    {
        var clock = new SimulatedClock(0);
        var inputs = new SimulatedActionExecutor(clock);
        var engine = new EarlyBuffPunishEngine(inputs, clock, new EarlyPunishOptions
        {
            EligibleSources = [SubstitutionBuffs.Tai, SubstitutionBuffs.Water],
            FirstToSecondMs = 0, SecondToThirdMs = 200
        });
        return (engine, clock, inputs);
    }

    [Fact]
    public void NormalDecoyRemovalNeverStartsComboOrClaimsPriority()
    {
        var (engine, clock, inputs) = Rig();
        engine.Update(Frame(0, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(100);
        Assert.False(engine.HasUnprocessedRemoval(Frame(100)));
        engine.Update(Frame(100), true, true, true);
        clock.Advance(100);
        engine.Update(Frame(200), true, true, true);
        Assert.Empty(inputs.History);
        Assert.Empty(engine.TriggerSources);
        Assert.False(engine.BlocksAutoTurning);
    }

    [Theory]
    [InlineData(SubstitutionBuffs.Tai)]
    [InlineData(SubstitutionBuffs.Water)]
    public void EligibleRemovalTriggersEvenIfNormalDecoyRemains(string eligible)
    {
        var (engine, clock, inputs) = Rig();
        engine.Update(Frame(0, SubstitutionBuffs.Normal, eligible), true, true, true);
        clock.Advance(100);
        engine.Update(Frame(100, SubstitutionBuffs.Normal), true, true, true);
        clock.Advance(100);
        engine.Update(Frame(200, SubstitutionBuffs.Normal), true, true, true);
        Assert.Equal(eligible, Assert.Single(engine.TriggerSources));
        Assert.Equal(new[] { "A", "Q" }, inputs.History.Select(h => h.Key));
    }

    [Fact]
    public void RemovingOnlyNormalWhileTaiRemainsDoesNotTrigger()
    {
        var (engine, clock, inputs) = Rig();
        engine.Update(Frame(0, SubstitutionBuffs.Normal, SubstitutionBuffs.Tai), true, true, true);
        clock.Advance(100);
        engine.Update(Frame(100, SubstitutionBuffs.Tai), true, true, true);
        clock.Advance(100);
        engine.Update(Frame(200, SubstitutionBuffs.Tai), true, true, true);
        Assert.Empty(inputs.History);
        Assert.Empty(engine.TriggerSources);
    }

    [Fact]
    public void RestrictedSourcesCannotIncludeNormal()
    {
        var (clock, inputs) = (new SimulatedClock(0), new SimulatedActionExecutor(new SimulatedClock(0)));
        Assert.Throws<ArgumentException>(() => new EarlyBuffPunishEngine(inputs, clock,
            new EarlyPunishOptions { EligibleSources = [SubstitutionBuffs.Normal] }));
    }
}
