using System.Globalization;

namespace MoaMat.Domain.Accounts;

/// <summary>
/// Derives the short avatar label shown next to a signed-in user. Purely
/// defensive: the display name comes from an identity provider and may be
/// empty, punctuation-only, or an address with no local part.
/// </summary>
public static class UserInitials
{
    private const string Unknown = "?";

    private static readonly char[] Separators = ['.', '-', '_', ' ', '+'];

    /// <summary>
    /// Returns one or two upper-case initials for <paramref name="displayName"/>,
    /// or <c>"?"</c> when nothing usable can be extracted.
    /// </summary>
    /// <param name="displayName">Display name or e-mail address; may be null.</param>
    public static string FromDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Unknown;
        }

        var localPart = displayName.Split('@')[0];
        var words = localPart.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

        var letters = words
            .Select(word => word.FirstOrDefault(char.IsLetterOrDigit))
            .Where(character => character != default)
            .Take(2)
            .Select(character => char.ToUpper(character, CultureInfo.InvariantCulture))
            .ToArray();

        return letters.Length == 0 ? Unknown : new string(letters);
    }
}
