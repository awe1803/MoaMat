using MoaMat.Domain.Common;

namespace MoaMat.Domain.Preferences;

/// <summary>
/// Port storing the display preferences of the signed-in account - the choices
/// that must outlive a browser, such as "never show the install tip again".
/// </summary>
/// <remarks>
/// Preferences belong to the account rather than to the device: the member who
/// answered "never again" expects it to hold on their phone as well as on the
/// club computer. Writes are idempotent - replaying the same choice converges
/// on the same state.
/// </remarks>
public interface IUserPreferenceRepository
{
    /// <summary>
    /// True when the signed-in account asked never to see the "install MoaMat
    /// on this device" tip again. False when no choice was ever stored.
    /// </summary>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    /// <exception cref="DataAccessException">The preference could not be read.</exception>
    Task<bool> IsInstallTipHiddenAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores, or clears, the "never show the install tip again" choice.</summary>
    /// <param name="isHidden">True to stop offering the tip, false to offer it again.</param>
    /// <param name="cancellationToken">Cancels the pending request.</param>
    Task<OperationResult> SetInstallTipHiddenAsync(bool isHidden, CancellationToken cancellationToken = default);
}
