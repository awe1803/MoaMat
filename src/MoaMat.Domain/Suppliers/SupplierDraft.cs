using MoaMat.Domain.Common;

namespace MoaMat.Domain.Suppliers;

/// <summary>
/// Validated contact details of a supplier, ready to be created or saved. The
/// value object carries the invariants (trimmed, non-empty name, lengths within
/// the column limits, plausible e-mail, http(s) website) so a malformed value is
/// refused with a clear message instead of reaching the database.
/// </summary>
public readonly record struct SupplierDraft
{
    /// <summary>Maximum length of the name.</summary>
    public const int NameMaxLength = 120;

    /// <summary>Maximum length of the address.</summary>
    public const int AddressMaxLength = 300;

    /// <summary>Maximum length of the phone number.</summary>
    public const int PhoneMaxLength = 40;

    /// <summary>Maximum length of the e-mail address.</summary>
    public const int EmailMaxLength = 200;

    /// <summary>Maximum length of the contact person.</summary>
    public const int ContactPersonMaxLength = 120;

    /// <summary>Maximum length of the website address.</summary>
    public const int WebsiteMaxLength = 300;

    private SupplierDraft(
        string name,
        string? address,
        string? phone,
        string? email,
        string? contactPerson,
        string? websiteUrl)
    {
        Name = name;
        Address = address;
        Phone = phone;
        Email = email;
        ContactPerson = contactPerson;
        WebsiteUrl = websiteUrl;
    }

    /// <summary>Trimmed, non-empty name.</summary>
    public string Name { get; }

    /// <summary>Trimmed address, or null when left blank.</summary>
    public string? Address { get; }

    /// <summary>Trimmed phone number, or null when left blank.</summary>
    public string? Phone { get; }

    /// <summary>Trimmed e-mail address, or null when left blank.</summary>
    public string? Email { get; }

    /// <summary>Trimmed contact person, or null when left blank.</summary>
    public string? ContactPerson { get; }

    /// <summary>Absolute http(s) address, or null when left blank.</summary>
    public string? WebsiteUrl { get; }

    /// <summary>Validates and builds a draft from the raw form values.</summary>
    /// <param name="name">Supplier name (required).</param>
    /// <param name="address">Postal address (optional).</param>
    /// <param name="phone">Phone number (optional).</param>
    /// <param name="email">E-mail address (optional).</param>
    /// <param name="contactPerson">Contact person (optional).</param>
    /// <param name="websiteUrl">Website (optional); <c>https://</c> is assumed when no scheme is typed.</param>
    /// <param name="draft">The validated draft when the method returns a success.</param>
    /// <returns>A success, or a failure carrying a message to display as-is.</returns>
    public static OperationResult TryCreate(
        string? name,
        string? address,
        string? phone,
        string? email,
        string? contactPerson,
        string? websiteUrl,
        out SupplierDraft draft)
    {
        draft = default;

        var trimmedName = Clean(name);
        if (trimmedName is null)
        {
            return OperationResult.Failure("Le nom du fournisseur est obligatoire.");
        }

        var tooLong = FirstTooLong(
            ("Le nom", trimmedName, NameMaxLength),
            ("L'adresse", Clean(address), AddressMaxLength),
            ("Le téléphone", Clean(phone), PhoneMaxLength),
            ("L'email", Clean(email), EmailMaxLength),
            ("La personne de contact", Clean(contactPerson), ContactPersonMaxLength),
            ("Le site web", Clean(websiteUrl), WebsiteMaxLength));
        if (tooLong is not null)
        {
            return OperationResult.Failure(tooLong);
        }

        var cleanEmail = Clean(email);
        if (cleanEmail is not null && !IsPlausibleEmail(cleanEmail))
        {
            return OperationResult.Failure("L'adresse email n'est pas valide.");
        }

        var cleanWebsite = Clean(websiteUrl);
        if (cleanWebsite is not null && !TryNormaliseWebsite(cleanWebsite, out cleanWebsite))
        {
            return OperationResult.Failure("Le site web doit être une adresse http(s) valide.");
        }

        draft = new SupplierDraft(
            trimmedName,
            Clean(address),
            Clean(phone),
            cleanEmail,
            Clean(contactPerson),
            cleanWebsite);
        return OperationResult.Success;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstTooLong(params (string Label, string? Value, int Max)[] fields)
    {
        foreach (var (label, value, max) in fields)
        {
            if (value is not null && value.Length > max)
            {
                return $"{label} ne peut pas dépasser {max} caractères.";
            }
        }

        return null;
    }

    private static bool IsPlausibleEmail(string value)
    {
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at <= 0 || at != value.LastIndexOf('@') || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var domain = value[(at + 1)..];
        var dot = domain.LastIndexOf('.');
        return dot > 0 && dot < domain.Length - 1;
    }

    private static bool TryNormaliseWebsite(string value, out string normalised)
    {
        normalised = value;

        if (value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var candidate = value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        normalised = candidate;
        return true;
    }
}
