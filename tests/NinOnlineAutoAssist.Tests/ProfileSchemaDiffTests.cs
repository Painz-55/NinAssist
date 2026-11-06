using NinOnlineAutoAssist.Diagnostics;
using static NinOnlineAutoAssist.Diagnostics.FieldMetadata;

namespace NinOnlineAutoAssist.Tests;

public sealed class ProfileSchemaDiffTests
{
    [Fact]
    public void StableTypeStructureRanksAboveObfuscatedNamesAndChangedLayout()
    {
        var old = new TypeEntry("Old", [new("abc", "System.Int32"), new("def", "System.String"),
            new("ghi", "Unknown.OLD")]);
        var current = new TypeEntry("New", [new("xyz", "System.Int32"), new("qwe", "System.String"),
            new("rty", "Unknown.NEW")]);
        var changed = new TypeEntry("Other", [new("xyz", "System.String"), new("qwe", "System.Int32"),
            new("rty", "Unknown.NEW")]);
        Assert.True(ProfileSchemaDiff.Similarity(old, current) > ProfileSchemaDiff.Similarity(old, changed));
        Assert.True(ProfileSchemaDiff.Similarity(old, current) >
            ProfileSchemaDiff.Similarity(old, new TypeEntry("Short", [new("abc", "System.Int32")])));
    }
}
