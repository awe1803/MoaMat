using System.Globalization;
using Microsoft.AspNetCore.Components;
using MoaMat.Domain.Common;
using MoaMat.Domain.Cylinders;
using MoaMat.Domain.Suppliers;

namespace MoaMat.Web.Pages;

/// <summary>
/// Suppliers: contact details, the services each one offers and the history of
/// their prices.
/// </summary>
/// <remarks>
/// Every value goes through <see cref="SupplierDraft"/> or
/// <see cref="SupplierTariffDraft"/> before it reaches the repository. Rights are
/// enforced by the database; the markup only hides the controls a role cannot
/// use. A price is never edited: a change is a new price with a later effective
/// date, which keeps the history.
/// </remarks>
public partial class SupplierManagement : ComponentBase
{
    private IReadOnlyList<Supplier> _suppliers = [];
    private IReadOnlyList<SupplierTariff> _tariffs = [];

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

    private string _tariffService = string.Empty;
    private decimal? _tariffPrice;
    private DateOnly? _tariffDate = DateOnly.FromDateTime(DateTime.Today);

    private bool _isBusy;
    private string? _error;

    [Inject]
    private ISupplierRepository Suppliers { get; set; } = default!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private Supplier? Selected => _suppliers.FirstOrDefault(supplier => supplier.Id == _selectedId);

    private IEnumerable<Supplier> Visible => _suppliers.Where(supplier => _showInactive || supplier.IsActive);

    /// <inheritdoc />
    protected override Task OnInitializedAsync() => ReloadAsync();

    /// <summary>Price in force today for a service of the selected supplier, if any.</summary>
    private SupplierTariff? CurrentTariff(CylinderServiceType service) =>
        _tariffs
            .Where(tariff => tariff.Service == service && tariff.EffectiveOn <= Today)
            .MaxBy(tariff => tariff.EffectiveOn);

    /// <summary>The next scheduled price of a service, if one is dated in the future.</summary>
    private SupplierTariff? UpcomingTariff(CylinderServiceType service) =>
        _tariffs
            .Where(tariff => tariff.Service == service && tariff.EffectiveOn > Today)
            .MinBy(tariff => tariff.EffectiveOn);

    private bool HasTariffs(CylinderServiceType service) => _tariffs.Any(tariff => tariff.Service == service);

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
            _tariffs = [];
        }
    }

    private async Task ReloadTariffsAsync()
    {
        if (_selectedId is not { } id)
        {
            _tariffs = [];
            return;
        }

        try
        {
            _tariffs = await Suppliers.GetTariffsAsync(id);
        }
        catch (DataAccessException exception)
        {
            _error = exception.Message;
        }
    }

    private async Task SelectAsync(long supplierId)
    {
        _selectedId = supplierId;
        _showForm = false;
        _confirmDelete = false;
        _error = null;
        _tariffs = [];
        _tariffService = string.Empty;
        _tariffPrice = null;
        await ReloadTariffsAsync();
    }

    private void StartNew()
    {
        _selectedId = null;
        _tariffs = [];
        _isNew = true;
        _showForm = true;
        _confirmDelete = false;
        _error = null;
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
            _tariffs = [];
            await ReloadAsync();
        }
    }

    private async Task ToggleServiceAsync(CylinderServiceType service, bool offered)
    {
        if (Selected is not { } supplier)
        {
            return;
        }

        var succeeded = await WriteAsync(() => offered
            ? Suppliers.AddServiceAsync(supplier.Id, service)
            : Suppliers.RemoveServiceAsync(supplier.Id, service));

        // Always reload: on a refusal the checkbox must snap back to the stored state.
        await ReloadAsync();
        if (succeeded && !offered && _tariffService == service.Code)
        {
            _tariffService = string.Empty;
        }
    }

    private async Task AddTariffAsync()
    {
        if (Selected is not { } supplier)
        {
            return;
        }

        var validation = SupplierTariffDraft.TryCreate(
            CylinderServiceType.FromCode(_tariffService),
            _tariffPrice,
            _tariffDate,
            out var draft);
        if (!validation.Succeeded)
        {
            _error = validation.Error;
            return;
        }

        if (await WriteAsync(() => Suppliers.AddTariffAsync(supplier.Id, draft)))
        {
            _tariffPrice = null;
            await ReloadTariffsAsync();
        }
    }

    private async Task DeleteTariffAsync(long tariffId)
    {
        if (await WriteAsync(() => Suppliers.DeleteTariffAsync(tariffId)))
        {
            await ReloadTariffsAsync();
        }
    }

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
}
