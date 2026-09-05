using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MoaMat.Web;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// --- Supabase -------------------------------------------------------------------
var supabaseSettings = builder.Configuration
    .GetSection(SupabaseSettings.SectionName)
    .Get<SupabaseSettings>() ?? new SupabaseSettings();

builder.Services.AddSingleton(supabaseSettings);

builder.Services.AddSingleton(_ =>
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

var host = builder.Build();

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
