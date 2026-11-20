using System;
using System.Linq;

namespace NinOnlineAutoAssist.Live;

public sealed class LocalCharacterIdentity
{
	public string? Name { get; private set; }

	public bool IsInvalidated { get; private set; }

	public LocalCharacterIdentity(string? expectedName = null)
	{
		if (!string.IsNullOrWhiteSpace(expectedName))
		{
			if (!IsValidName(expectedName))
			{
				throw new ArgumentException("Nome do personagem inválido.", "expectedName");
			}
			Name = expectedName;
		}
	}

	public static bool IsValidName(string? name)
	{
		if (name != null)
		{
			int length = name.Length;
			if (length > 0 && length <= 64 && !string.IsNullOrWhiteSpace(name) && name == name.Trim())
			{
				return !name.Any(char.IsControl);
			}
		}
		return false;
	}

	public bool ConfirmStableObservation(string? first, string? repeated)
	{
		if (IsInvalidated || !IsValidName(first) || first != repeated)
		{
			return false;
		}
		if (Name != null && Name != first)
		{
			IsInvalidated = true;
			return false;
		}
		if (Name == null)
		{
			string text = (Name = first);
		}
		return true;
	}
}
