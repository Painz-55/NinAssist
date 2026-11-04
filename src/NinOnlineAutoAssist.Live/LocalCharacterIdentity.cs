namespace NinOnlineAutoAssist.Live;

/// <summary>Pin one local identity per connection, never select a nearby player or silently switch characters.</summary>
public sealed class LocalCharacterIdentity
{
    public string? Name { get; private set; }
    public bool IsInvalidated { get; private set; }
    public LocalCharacterIdentity(string? expectedName = null)
    {
        if (string.IsNullOrWhiteSpace(expectedName)) return;
        if (!IsValidName(expectedName)) throw new ArgumentException("Nome do personagem inválido.", nameof(expectedName));
        Name = expectedName;
    }
    public static bool IsValidName(string? name) => name is { Length: > 0 and <= 64 } &&
        !string.IsNullOrWhiteSpace(name) && name == name.Trim() && !name.Any(char.IsControl);
    public bool ConfirmStableObservation(string? first, string? repeated)
    {
        if (IsInvalidated || !IsValidName(first) || first != repeated) return false;
        if (Name is not null && Name != first)
        {
            IsInvalidated = true;
            return false;
        }
        Name ??= first;
        return true;
    }
}
