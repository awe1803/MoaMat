using Microsoft.Extensions.DependencyInjection;
using MoaMat.Domain.Accounts;
using MoaMat.Domain.Audit;
using MoaMat.Domain.Authentication;
using MoaMat.Domain.Inventory;
using MoaMat.Domain.Locations;
using MoaMat.Infrastructure.Supabase;

namespace MoaMat.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the Supabase adapters behind the domain ports. The host binds the
/// settings and supplies the session store; it never names a concrete adapter,
/// so swapping the backing store touches this file only.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers every driven adapter. The <see cref="global::Supabase.Client"/>
    /// itself is expected to be registered by the host, which owns the browser
    /// specific session store.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddMoaMatInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IAuthenticationService, SupabaseAuthenticationService>();
        services.AddScoped<IAccountRepository, SupabaseAccountRepository>();
        services.AddScoped<IAuditLogRepository, SupabaseAuditLogRepository>();
        services.AddScoped<IInventoryRepository, SupabaseInventoryRepository>();
        services.AddScoped<ILocationRepository, SupabaseLocationRepository>();

        return services;
    }
}
