using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MoaMat.Domain.Accounts;
using MoaMat.Domain.Navigation;
using MoaMat.Infrastructure.Supabase;
using MoaMat.Web.Authentication;
using MoaMat.Web.Connectivity;
using MoaMat.Web.Navigation;
using MoaMat.Web.Notifications;
using MoaMat.Web.Pwa;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace MoaMat.Web.DependencyInjection;

/// <summary>
/// Composition root helpers for the Blazor host: the browser-specific services
/// and the authorization policies. Keeping them here leaves
/// <c>Program.cs</c> readable as a list of intentions.
/// </summary>
internal static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Supabase client together with the browser session store and
    /// the Blazor authentication state provider.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <param name="settings">Validated Supabase settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddSupabaseClient(
        this IServiceCollection services,
        SupabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        services.AddSingleton(settings);
        services.AddScoped<IApplicationUrlProvider, BlazorApplicationUrlProvider>();

        services.AddSingleton<IGotrueSessionPersistence<Session>>(provider =>
            new BrowserSessionPersistence(provider.GetRequiredService<IJSRuntime>()));

        services.AddSingleton(provider => SupabaseClientFactory.Create(
            settings,
            provider.GetRequiredService<IGotrueSessionPersistence<Session>>()));

        services.AddScoped<SupabaseAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<SupabaseAuthenticationStateProvider>());

        return services;
    }

    /// <summary>
    /// Registers the Web Push opt-in. With no VAPID key configured the service
    /// still resolves, and reports the feature as not configured.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <param name="settings">Validated push settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPushNotifications(
        this IServiceCollection services,
        PushNotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        services.AddSingleton(settings);
        services.AddScoped<PushNotificationService>();

        return services;
    }

    /// <summary>
    /// Registers the "install MoaMat on this device" tip. It needs no
    /// configuration of its own: the browser answers what it can do, and the
    /// stored choice comes from the infrastructure preference store.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPwaInstallTip(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<PwaInstallService>();

        return services;
    }

    /// <summary>
    /// Registers the "a new version is available" notice. It needs no
    /// configuration of its own: the service worker registered by
    /// <c>index.html</c> is what it watches.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddPwaUpdateNotice(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<PwaUpdateService>();

        return services;
    }

    /// <summary>
    /// Registers the "no internet access" page. It probes the Supabase project
    /// rather than an address of our own: reaching the server the application
    /// actually needs is the only answer worth showing the user.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <param name="settings">Validated Supabase settings.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOfflineGate(
        this IServiceCollection services,
        SupabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        services.AddScoped(provider => new ConnectivityService(
            provider.GetRequiredService<IJSRuntime>(),
            settings.Url));

        return services;
    }

    /// <summary>
    /// Registers the "at least this role" policies used by the router and the
    /// navigation menu.
    /// </summary>
    /// <param name="services">Container being configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddMoaMatAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(
                AuthorizationPolicyNames.ReaderOrHigher,
                policy => policy.RequireAssertion(RequireAtLeast(AppRole.Reader)));
            options.AddPolicy(
                AuthorizationPolicyNames.ManagerOrHigher,
                policy => policy.RequireAssertion(RequireAtLeast(AppRole.Manager)));
            options.AddPolicy(
                AuthorizationPolicyNames.AdministratorOrHigher,
                policy => policy.RequireAssertion(RequireAtLeast(AppRole.Administrator)));
            options.AddPolicy(
                AuthorizationPolicyNames.SuperAdministrator,
                policy => policy.RequireAssertion(RequireAtLeast(AppRole.SuperAdministrator)));
        });

        services.AddCascadingAuthenticationState();

        return services;
    }

    /// <summary>Reads the role claim of the current principal.</summary>
    /// <param name="user">Principal being evaluated.</param>
    public static AppRole RoleOf(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return AppRole.FromCode(user.FindFirst(ClaimTypes.Role)?.Value);
    }

    private static Func<AuthorizationHandlerContext, bool> RequireAtLeast(AppRole minimum) =>
        context => context.User.RoleOf().IsAtLeast(minimum);
}
