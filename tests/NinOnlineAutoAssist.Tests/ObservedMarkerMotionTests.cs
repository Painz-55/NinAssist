using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.Tests;

public sealed class ObservedMarkerMotionTests
{
    [Fact]
    public void InterpolatesOnlyBetweenObservedEndpointsAndNeverPredictsPastThem()
    {
        var motion = new ObservedMarkerMotion();
        motion.Set(77, "KageSlayer", 100, 200, 1000, true);
        motion.Set(77, "KageSlayer", 200, 200, 1100, true);
        Assert.Equal((100d, 200d), motion.Position(77, 1100));
        Assert.InRange(motion.Position(77, 1165)!.Value.X, 140, 160);
        Assert.Equal((200d, 200d), motion.Position(77, 1500));
        motion.Set(77, "KageSlayer", 200, 200, 1700, true);
        Assert.Equal((200d, 200d), motion.Position(77, 9000));
        motion.KeepOnly(Array.Empty<int>());
        Assert.Null(motion.Position(77, 9000));
    }

    [Fact]
    public void NewIdentitySnapsInsteadOfAnimatingAcrossPlayers()
    {
        var motion = new ObservedMarkerMotion();
        motion.Set(77, "Old", 100, 100, 0, true);
        motion.Set(77, "New", 500, 500, 10, true);
        Assert.Equal((500d, 500d), motion.Position(77, 10));
    }
}
