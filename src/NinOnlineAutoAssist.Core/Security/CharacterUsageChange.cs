namespace NinOnlineAutoAssist.Core.Security;

public static class FirebaseUsagePolicy
{
    // This is only a usage-reporting exemption, NOT an administrator role or authorization grant.
    // Call with the email returned by Firebase Auth, never the editable login textbox/preference file.
    public static bool IsExempt(string authenticatedEmail) =>
        string.Equals(authenticatedEmail.Trim(), "pain@ninassist.com.br", StringComparison.OrdinalIgnoreCase);
}

public sealed record CharacterUsageChange(long Sequence, string PreviousCharacterName, string CharacterName)
{
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 64 && !name.Any(char.IsControl);
    public void Validate()
    {
        if (Sequence <= 0 || !ValidName(CharacterName) ||
            (PreviousCharacterName != "" && !ValidName(PreviousCharacterName)) || CharacterName == PreviousCharacterName)
            throw new ArgumentException("Alteração de personagem inválida.");
    }
}
