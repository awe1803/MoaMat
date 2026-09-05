using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
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

builder.Services.AddSingleton(_ => new Supabase.Client(
    supabaseSettings.Url,
    supabaseSettings.AnonKey,
    new Supabase.SupabaseOptions
    {
        AutoConnectRealtime = true,
        AutoRefreshToken = true,
    }));

var host = builder.Build();

// Initialise le client Supabase (récupère la session éventuelle, ouvre le realtime).
await host.Services.GetRequiredService<Supabase.Client>().InitializeAsync();

await host.RunAsync();
