namespace MoaMat.Domain.Suppliers;

/// <summary>
/// A provider of the club (service company, parts vendor) with its contact
/// details. The services it offers are read separately
/// (<see cref="ISupplierRepository.GetServicesAsync"/>).
/// </summary>
/// <param name="Id">Database identifier.</param>
/// <param name="Name">Display name, unique ignoring case.</param>
/// <param name="Address">Postal address, free text.</param>
/// <param name="Phone">Phone number, free text.</param>
/// <param name="Email">Contact e-mail address.</param>
/// <param name="ContactPerson">Person to ask for.</param>
/// <param name="WebsiteUrl">Absolute http(s) address of the provider's website.</param>
/// <param name="IsActive">False once the provider is archived; history is kept.</param>
public sealed record Supplier(
    long Id,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? ContactPerson,
    string? WebsiteUrl,
    bool IsActive);
