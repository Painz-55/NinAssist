using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class UpdatedBuildCastEvidenceTests
{
    [Theory]
    [InlineData(241, 6175443, 6176433, 6175496, 990)]
    [InlineData(235, 6176849, 6177839, 6177013, 990)]
    [InlineData(134, 6178425, 6179415, 6178451, 990)]
    [InlineData(131, 6180260, 6181250, 6180354, 990)]
    [InlineData(1163, 6182056, 6184046, 6182106, 1990)]
    [InlineData(1150, 6189955, 6191945, 6190046, 1990)]
    public void ReadOnlyCastDecoderAcceptsCorrelatedSamples(int spell, int start, int end, long clock, long total)
    {
        var observed = CastObservation.Decode(spell, start, spell, end, clock);
        Assert.NotNull(observed);
        Assert.True(observed.Valid);
        Assert.Equal(spell, observed.SpellId);
        Assert.Equal(total, observed.TotalMs);
        Assert.Equal(end - clock, observed.RemainingMs);
    }
}
