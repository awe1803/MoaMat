using System.Globalization;
using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Common;
using MoaMat.Domain.Suppliers;

namespace MoaMat.Web.Pages;

/// <summary>
/// Suppliers: contact details and archiving, the services each one offers (bottle
/// controls — optical, or hydraulic with an explicit oil/water method — or free
/// services) and the history of their prices, which are optional.
/// </summary>
/// <remarks>
/// Every value goes through <see cref="SupplierDraft"/>,
/// <see cref="SupplierServiceRequest"/> or <see cref="SupplierTariffDraft"/>
/// before it reaches the repository. Rights are enforced by the database; the
/// markup only hides the controls a role cannot use. A price is never edited: a
/// change is a new price with a later effective date, which keeps the history.
/// A hydraulic service always names oil or water — the choice is never defaulted.
/// </remarks>
public partial class SupplierManagement : ComponentBase
{
    private const string NoControlValue = "";

    private IReadOnlyList<Supplier> _suppliers = [];
    private IReadOnlyList<SupplierService> _services = [];

    private long? _selectedId;
    private bool _showForm;
    private bool _isNew;
    private bool _confirmDelete;
    private bool _showInactive;

    private string _name = string.Empty;
    private string _address = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _contactPerson = string.Empty;
    private string _website = string.Empty;

    // Service form (create when _editingServiceId is null).
    private bool _showServiceForm;
    private long? _editingServiceId;
    private string _serviceName = string.Empty;
    private string _serviceControl = NoControlValue;
    private string _serviceMethod = string.Empty;
    private bool _serviceActive = true;
    private string _serviceComment = string.Empty;

    // Tariff panel of one service.
    private long? _tariffServiceId;
    private decimal? _tariffPrice;
    private DateOnly? _tariffDate = DateOnly.FromDateTime(DateTime.Today);

    private long? _pendingDeleteServiceId;

    private bool _isBusy;
    private string? _error;
    private string? _notice;

    [Inject]
    private ISupplierRepository Suppliers { get; set; } = default!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private Supplier? Selected => _suppliers.FirstOrDefault(supplier => supplier.Id == _selectedId);

    private IEnumerable<Supplier> Visible => _suppliers.Where(supplier => _showInactive || supplier.IsActive);

    private SupplierService? TariffService => _services.FirstOrDefault(service => service.Id == _tariffServiceId);

    private bool IsHydraulicSelected => _serviceControl == nameof(CylinderControl.Hydraulic);

    /// <inheritdoc />
    protected override Task OnInitializedAsync() => ReloadAsync();

    /// <summary>Price in force today for a service, if any.</summary>
    private static ServicePriceRule? CurrentTariff(SupplierService service) =>
        service.Tariffs
            .Where(tariff => tariff.EffectiveOn <= Today)
            .MaxBy(tariff => tariff.EffectiveOn);

    /// <summary>The next scheduled price of a service, if one is dated in the future.</summary>
    private static ServicePriceRule? UpcomingTariff(SupplierService service) =>
        service.Tariffs
            .Where(tariff => tariff.EffectiveOn > Today)
            .MinBy(tariff => tariff.EffectiveOn);

    /// <summary>Builds a <c>tel:</c> link keeping only the digits and a leading plus.</summary>
    private static string PhoneHref(string phone) =>
        "tel:" + new string([.. phone.Where(character => char.IsAsciiDigit(character) || character == '+')]);

    private static string FormatPrice(decimal price) =>
        price.ToString("N2", CultureInfo.GetCultureInfo("fr-BE")) + " €";

    private static string FormatDate(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private async Task ReloadAsync()
    {
        _isBusy = true;
        _error = null;

        try
        {
            _suppliers = await Suppliers.GetSuppliersAsync();
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
        }
        finally
        {
            _isBusy = false;
        }

        if (Selected is null)
        {
            _selectedId = null;
            _services = [];
        }
    }

    private async Task ReloadServicesAsync()
    {
        if (_selectedId is not { } id)
        {
            _services = [];
            return;
        }

        try
        {
            _services = await Suppliers.GetServicesAsync(id);
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
        }
    }

    private void CloseSubForms()
    {
        _showServiceForm = false;
        _tariffServiceId = null;
        _pendingDeleteServiceId = null;
    }

    private async Task SelectAsync(long supplierId)
    {
        _selectedId = supplierId;
        _showForm = false;
        _confirmDelete = false;
        _error = null;
        _notice = null;
        _services = [];
        CloseSubForms();
        await ReloadServicesAsync();
    }

    // ----- Supplier -----------------------------------------------------------

    private void StartNew()
    {
        _selectedId = null;
        _services = [];
        _isNew = true;
        _showForm = true;
        _confirmDelete = false;
        _error = null;
        _notice = null;
        CloseSubForms();
        FillForm(null);
    }

    private void StartEdit()
    {
        if (Selected is not { } supplier)
        {
            return;
        }

        _isNew = false;
        _showForm = true;
        _error = null;
        _notice = null;
        FillForm(supplier);
    }

    private void CancelForm()
    {
        _showForm = false;
        _error = null;
    }

    private void FillForm(Supplier? supplier)
    {
        _name = supplier?.Name ?? string.Empty;
        _address = supplier?.Address ?? string.Empty;
        _phone = supplier?.Phone ?? string.Empty;
        _email = supplier?.Email ?? string.Empty;
        _contactPerson = supplier?.ContactPerson ?? string.Empty;
        _website = supplier?.WebsiteUrl ?? string.Empty;
    }

    private async Task SaveAsync()
    {
        var validation = SupplierDraft.TryCreate(_name, _address, _phone, _email, _contactPerson, _website, out var draft);
        if (!validation.Succeeded)
        {
            _error = validation.Error;
            return;
        }

        var wasNew = _isNew;
        var succeeded = await WriteAsync(() => wasNew
            ? Suppliers.AddSupplierAsync(draft)
            : Suppliers.UpdateSupplierAsync(_selectedId!.Value, draft));
        if (!succeeded)
        {
            return;
        }

        _showForm = false;
        await ReloadAsync();

        if (wasNew)
        {
            // The new row has no id yet on the client: select it by its (unique) name.
            var created = _suppliers.FirstOrDefault(supplier =>
                string.Equals(supplier.Name, draft.Name, StringComparison.OrdinalIgnoreCase));
            if (created is not null)
            {
                await SelectAsync(created.Id);
            }
        }
    }

    private async Task ToggleActiveAsync()
    {
        if (Selected is not { } supplier)
        {
            return;
        }

        if (await WriteAsync(() => Suppliers.SetSupplierActiveAsync(supplier.Id, !supplier.IsActive)))
        {
            await ReloadAsync();
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not { } supplier)
        {
            return;
        }

        if (!_confirmDelete)
        {
            _confirmDelete = true;
            return;
        }

        _confirmDelete = false;
        if (await WriteAsync(() => Suppliers.DeleteSupplierAsync(supplier.Id)))
        {
            _selectedId = null;
            _services = [];
            await ReloadAsync();
        }
    }

    // ----- Services -------------------------------------------------------------

    private void StartNewService()
    {
        CloseSubForms();
        _error = null;
        _notice = null;
        _editingServiceId = null;
        _serviceName = string.Empty;
        _serviceControl = NoControlValue;
        _serviceMethod = string.Empty;
        _serviceActive = true;
        _serviceComment = string.Empty;
        _showServiceForm = true;
    }

    private void StartEditService(SupplierService service)
    {
        CloseSubForms();
        _error = null;
        _notice = null;
        _editingServiceId = service.Id;
        _serviceName = service.Name;
        _serviceControl = service.Control?.ToString() ?? NoControlValue;
        _serviceMethod = service.Method?.ToString() ?? string.Empty;
        _serviceActive = service.IsActive;
        _serviceComment = service.Comment ?? string.Empty;
        _showServiceForm = true;
    }

    private async Task SaveServiceAsync()
    {
        if (Selected is not { } supplier)
        {
            return;
        }

        var control = Enum.TryParse<CylinderControl>(_serviceControl, out var parsedControl) ? parsedControl : (CylinderControl?)null;
        var method = Enum.TryParse<HydraulicMethod>(_serviceMethod, out var parsedMethod) ? parsedMethod : (HydraulicMethod?)null;

        var request = SupplierServiceRequest.Create(
            _editingServiceId,
            supplier.Id,
            _serviceName,
            control,
            method,
            _serviceActive,
            _serviceComment);

        if (!request.Succeeded)
        {
            _error = request.Error;
            return;
        }

        if (await WriteValueAsync(() => Suppliers.SaveServiceAsync(request.Value!)))
        {
            _showServiceForm = false;
            _notice = "Prestation enregistrée.";
            await ReloadServicesAsync();
        }
    }

    private async Task ToggleServiceActiveAsync(SupplierService service)
    {
        var request = SupplierServiceRequest.Create(
            service.Id,
            service.SupplierId,
            service.Name,
            service.Control,
            service.Method,
            !service.IsActive,
            service.Comment);

        if (!request.Succeeded)
        {
            _error = request.Error;
            return;
        }

        if (await WriteValueAsync(() => Suppliers.SaveServiceAsync(request.Value!)))
        {
            _notice = service.IsActive ? "Prestation désactivée." : "Prestation réactivée.";
            await ReloadServicesAsync();
        }
    }

    private void RequestDeleteService(SupplierService service) => _pendingDeleteServiceId = service.Id;

    private void CancelDeleteService() => _pendingDeleteServiceId = null;

    private async Task DeleteServiceAsync(SupplierService service)
    {
        var succeeded = await WriteAsync(() => Suppliers.DeleteServiceAsync(service.Id));
        _pendingDeleteServiceId = null;

        if (succeeded)
        {
            _notice = "Prestation supprimée.";
            await ReloadServicesAsync();
        }
    }

    // ----- Tariffs --------------------------------------------------------------

    private void StartTariff(SupplierService service)
    {
        CloseSubForms();
        _error = null;
        _notice = null;
        _tariffServiceId = service.Id;
        _tariffPrice = null;
        _tariffDate = Today;
    }

    private async Task AddTariffAsync()
    {
        var validation = SupplierTariffDraft.TryCreate(_tariffServiceId, _tariffPrice, _tariffDate, out var draft);
        if (!validation.Succeeded)
        {
            _error = validation.Error;
            return;
        }

        if (await WriteAsync(() => Suppliers.AddTariffAsync(draft)))
        {
            _tariffPrice = null;
            _notice = "Prix ajouté (l'historique des prix n'est jamais modifié).";
            await ReloadServicesAsync();
        }
    }

    private async Task DeleteTariffAsync(long tariffId)
    {
        if (await WriteAsync(() => Suppliers.DeleteTariffAsync(tariffId)))
        {
            await ReloadServicesAsync();
        }
    }

    // ----- Plumbing ---------------------------------------------------------------

    private async Task<bool> WriteAsync(Func<Task<OperationResult>> write)
    {
        _isBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var result = await write();
            if (!result.Succeeded)
            {
                _error = result.Error;
            }

            return result.Succeeded;
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task<bool> WriteValueAsync<TValue>(Func<Task<OperationResult<TValue>>> write)
    {
        _isBusy = true;
        _error = null;
        StateHasChanged();

        try
        {
            var result = await write();
            if (!result.Succeeded)
            {
                _error = result.Error;
            }

            return result.Succeeded;
        }
        finally
        {
            _isBusy = false;
        }
    }
}
