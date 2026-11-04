using System.Text.Json;
using NinOnlineAutoAssist.Core.Security;

namespace NinOnlineAutoAssist.Tests;

public sealed class LoginEmailMemoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nin-email-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "preferences.json");
    private LoginEmailMemory Memory => new(FilePath);

    [Fact]
    public void MissingPreferenceReturnsEmptyEmailWithoutCreatingAnyFile()
    {
        Assert.Equal("", Memory.Read()); Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void RemembersOnlyEmailAndLoadsItAcrossInstances()
    {
        Assert.True(Memory.TryRemember("  Player@example.test  "));
        Assert.Equal("Player@example.test", new LoginEmailMemory(FilePath).Read());
        using var document = JsonDocument.Parse(File.ReadAllText(FilePath));
        var property = Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal("LastEmail", property.Name); Assert.Equal("Player@example.test", property.Value.GetString());
        Assert.Single(Directory.GetFiles(_directory)); // Atomic temporary file was cleaned/moved.
    }

    [Fact]
    public void NewSuccessfulAccountReplacesOldEmailAndForgetClearsIt()
    {
        Assert.True(Memory.TryRemember("one@example.test"));
        Assert.True(Memory.TryRemember("two@example.test"));
        Assert.Equal("two@example.test", Memory.Read());
        Assert.True(Memory.TryForget()); Assert.Equal("", Memory.Read()); Assert.False(File.Exists(FilePath));
        Assert.True(Memory.TryForget());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an email")]
    [InlineData("Display Name <user@example.test>")]
    [InlineData("user\n@example.test")]
    public void InvalidValuesNeverOverwritePreviouslyRememberedEmail(string input)
    {
        Assert.True(Memory.TryRemember("valid@example.test"));
        Assert.False(Memory.TryRemember(input)); Assert.Equal("valid@example.test", Memory.Read());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"LastEmail\":123}")]
    [InlineData("{\"LastEmail\":\"not-an-email\"}")]
    public void BrokenPreferencesDoNotPreventLoginUiFromOpening(string json)
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(FilePath, json);
        Assert.Equal("", Memory.Read()); Assert.Equal(json, File.ReadAllText(FilePath));
    }

    [Fact]
    public void OversizedFileAndUnwritableDestinationFailWithoutExposingOrPersistingOtherData()
    {
        Directory.CreateDirectory(_directory); File.WriteAllText(FilePath, new string('x', 4097));
        Assert.Equal("", Memory.Read());
        var badDestination = Path.Combine(_directory, "directory-instead-of-file");
        Directory.CreateDirectory(badDestination);
        Assert.False(new LoginEmailMemory(badDestination).TryRemember("valid@example.test"));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
