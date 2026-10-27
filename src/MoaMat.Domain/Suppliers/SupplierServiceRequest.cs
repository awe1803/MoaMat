using MoaMat.Domain.Common;

namespace MoaMat.Domain.Suppliers;

/// <summary>
/// A service to create or modify on a supplier. Only <see cref="Create"/>
/// builds one, so an instance always satisfies the invariants below (the
/// database enforces the same rules again) — in particular a hydraulic service
/// always carries its oil/water method, and no other service ever does.
/// </summary>
public sealed record SupplierServiceRequest
{
    private SupplierServiceRequest()
    {
    }

    /// <summary>Service being modified; <c>null</c> to create a new one.</summary>
    public long? ServiceId { get; private init; }

    /// <summary>Supplier the service belongs to (only read on creation).</summary>
    public long SupplierId { get; private init; }

    /// <summary>Label, never blank.</summary>
    public string Name { get; private init; } = string.Empty;

    /// <summary>Bottle counter fed; <c>null</c> for a free service.</summary>
    public CylinderControl? Control { get; private init; }

    /// <summary>Oil/water bath — present if and only if <see cref="Control"/> is hydraulic.</summary>
    public HydraulicMethod? Method { get; private init; }

    /// <summary>False to deactivate the service.</summary>
    public bool IsActive { get; private init; }

    /// <summary>Free-text remark, when given.</summary>
    public string? Comment { get; private init; }

    /// <summary>Validates and builds a request.</summary>
    /// <param name="serviceId">Service being modified, or <c>null</c> to create.</param>
    /// <param name="supplierId">Supplier owning the service.</param>
    /// <param name="name">Label.</param>
    /// <param name="control">Bottle counter fed; <c>null</c> for a free service.</param>
    /// <param name="method">Oil/water bath; mandatory for hydraulic, forbidden otherwise.</param>
    /// <param name="isActive">Whether the service is offered in new campaigns.</param>
    /// <param name="comment">Remark, optional.</param>
    /// <returns>The request, or the reason it was refused.</returns>
    public static OperationResult<SupplierServiceRequest> Create(
        long? serviceId,
        long supplierId,
        string? name,
        CylinderControl? control,
        HydraulicMethod? method,
        bool isActive,
        string? comment)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return OperationResult<SupplierServiceRequest>.Failure("Le libellé de la prestation est obligatoire.");
        }

        // No default between oil and water: the choice is always explicit.
        if (control == CylinderControl.Hydraulic && method is null)
        {
            return OperationResult<SupplierServiceRequest>.Failure(
                "Une prestation hydraulique exige la méthode « huile » ou « eau ».");
        }

        if (control != CylinderControl.Hydraulic && method is not null)
        {
            return OperationResult<SupplierServiceRequest>.Failure(
                "La méthode huile/eau ne s'applique qu'à une prestation hydraulique.");
        }

        return OperationResult<SupplierServiceRequest>.Success(new SupplierServiceRequest
        {
            ServiceId = serviceId,
            SupplierId = supplierId,
            Name = name.Trim(),
            Control = control,
            Method = method,
            IsActive = isActive,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        });
    }
}
