using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MoaMat.Web;
using MoaMat.Web.Auth;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// --- Supabase -------------------------------------------------------------------
var supabaseSettings = builder.Configuration
    .GetSection(SupabaseSettings.SectionName)
    .Get<SupabaseSettings>() ?? new SupabaseSettings();

builder.Services.AddSingleton(supabaseSettings);

builder.Services.AddSingleton(sp =>
{
    // supabase-csharp construit sinon ses HttpClient via des factories qui affectent
    // HttpClientHandler.Proxy. Sur WebAssembly, BrowserHttpHandler.set_Proxy lève
    // PlatformNotSupportedException dès la construction des sous-clients (Functions,
    // Storage...). On injecte donc un HttpClient compatible navigateur partout.
    var browserHttpClient = new HttpClient();

    var options = new Supabase.SupabaseOptions
    {
        // Le client Realtime ouvre un WebSocket + des timers de reconnexion
        // incompatibles avec le runtime WASM : pas d'auto-connexion au démarrage.
        AutoConnectRealtime = false,
        AutoRefreshToken = true,

        // Session persistée dans le localStorage du navigateur : l'utilisateur
        // reste connecté d'un rechargement / d'une réouverture de la PWA à l'autre.
        SessionHandler = new BrowserSessionPersistence(sp.GetRequiredService<IJSRuntime>()),

        // Partagé par Auth / Postgrest / Functions.
        HttpClient = browserHttpClient,
    };

    // Storage gère ses propres HttpClient (requêtes, upload, download) et ignore
    // l'option ci-dessus : on les fournit explicitement pour éviter le proxy.
    options.StorageClientOptions.HttpRequestClient = browserHttpClient;
    options.StorageClientOptions.HttpUploadClient = browserHttpClient;
    options.StorageClientOptions.HttpDownloadClient = browserHttpClient;

    return new Supabase.Client(supabaseSettings.Url, supabaseSettings.AnonKey, options);
});

// --- Authentification / autorisation ------------------------------------------
builder.Services.AddScoped<SupabaseAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<SupabaseAuthenticationStateProvider>());
builder.Services.AddScoped<AuthService>();

// --- Accès aux données -------------------------------------------------------
builder.Services.AddScoped<MoaMat.Web.Data.AuditLogService>();

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy(MoaMatRoles.Policies.LectureOuPlus, p => p.RequireAssertion(HasRankAtLeast(1)));
    options.AddPolicy(MoaMatRoles.Policies.GestionOuPlus, p => p.RequireAssertion(HasRankAtLeast(2)));
    options.AddPolicy(MoaMatRoles.Policies.AdminOuPlus, p => p.RequireAssertion(HasRankAtLeast(3)));
    options.AddPolicy(MoaMatRoles.Policies.SuperAdmin, p => p.RequireAssertion(HasRankAtLeast(4)));

    static Func<Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext, bool> HasRankAtLeast(int min)
        => ctx => MoaMatRoles.Rank(ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value) >= min;
});
builder.Services.AddCascadingAuthenticationState();

var host = builder.Build();

// Garde-fou : la clé embarquée doit être une clé publique (anon / publishable),
// jamais une clé de service. Un JWT service_role décodé ici serait une fuite grave.
WarnIfNotAnonKey(supabaseSettings.AnonKey, host.Services.GetRequiredService<ILoggerFactory>());

// Initialise le client Supabase (récupère la session éventuelle). Un échec réseau
// au démarrage ne doit pas empêcher l'application de se charger.
try
{
    await host.Services.GetRequiredService<Supabase.Client>().InitializeAsync();
}
catch (Exception ex)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Supabase")
        .LogWarning(ex, "Initialisation Supabase impossible au démarrage.");
}

await host.RunAsync();

static void WarnIfNotAnonKey(string key, ILoggerFactory loggerFactory)
{
    var logger = loggerFactory.CreateLogger("Supabase");

    if (key.StartsWith("sb_secret_", StringComparison.Ordinal))
    {
        logger.LogError("La clé Supabase configurée est une clé SECRÈTE (sb_secret_…). "
            + "Seule une clé publishable / anon doit figurer côté client.");
        return;
    }

    // Clés « legacy » : JWT à 3 segments dont le payload porte un claim "role".
    var parts = key.Split('.');
    if (parts.Length != 3)
    {
        return;
    }

    try
    {
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        if (doc.RootElement.TryGetProperty("role", out var role)
            && role.GetString() is { } roleValue
            && !string.Equals(roleValue, "anon", StringComparison.Ordinal))
        {
            logger.LogError("La clé Supabase configurée a le rôle JWT '{Role}' (attendu : 'anon'). "
                + "Ne jamais publier une clé service_role côté client.", roleValue);
        }
    }
    catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
    {
        // Format inattendu : on n'empêche pas le démarrage pour autant.
    }
}
