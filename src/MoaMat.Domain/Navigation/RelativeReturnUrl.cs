namespace MoaMat.Domain.Navigation;

/// <summary>
/// A post-sign-in redirect target that is guaranteed to stay inside the
/// application. The value object is the invariant: there is no way to hold a
/// <see cref="RelativeReturnUrl"/> that points at another origin or at a
/// scheme-bearing URI, so no caller can accidentally open-redirect a user.
/// </summary>
/// <remarks>
/// Anything suspicious degrades to <see cref="Home"/> rather than throwing:
/// the candidate comes from a query string an attacker fully controls, and the
/// safe behaviour is to land the user on the home page.
/// </remarks>
public readonly record struct RelativeReturnUrl
{
    private RelativeReturnUrl(string value) => Value = value;

    /// <summary>Application-relative path, empty for the home page.</summary>
    public string Value { get; }

    /// <summary>The application root.</summary>
    public static RelativeReturnUrl Home { get; } = new(string.Empty);

    /// <summary>
    /// Accepts <paramref name="candidate"/> only if it is an application
    /// relative path; anything else yields <see cref="Home"/>.
    /// </summary>
    /// <param name="candidate">Raw value read from the query string.</param>
    public static RelativeReturnUrl FromCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Home;
        }

        var value = candidate.Trim();

        // Control characters can be used to smuggle a scheme past naive checks.
        if (value.Any(char.IsControl))
        {
            return Home;
        }

        // A leading slash or backslash makes the browser resolve the target
        // against the origin root - or, for "//host", against another origin.
        if (value[0] is '/' or '\\')
        {
            return Home;
        }

        // "https://evil", but also "javascript:..." and "data:...": any colon
        // before the first path separator means the value carries a scheme.
        var firstSeparator = value.IndexOfAny(['/', '?', '#']);
        var beforePath = firstSeparator < 0 ? value : value[..firstSeparator];
        if (beforePath.Contains(':', StringComparison.Ordinal))
        {
            return Home;
        }

        return Uri.TryCreate(value, UriKind.Relative, out _) ? new RelativeReturnUrl(value) : Home;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
