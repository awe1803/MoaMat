using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MoaMat.Infrastructure.DependencyInjection;
using MoaMat.Infrastructure.Supabase;
using MoaMat.Web;
using MoaMat.Web.DependencyInjection;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});

// Fail fast on a missing or unsafe configuration. Starting with a blank URL, or
// with a privileged key baked into a static bundle, is worse than not starting:
// the first symptom would otherwise be an opaque network error, or a silent leak.
var supabaseSettings = builder.Configuration
    .GetSection(SupabaseSettings.SectionName)
    .Get<SupabaseSettings>() ?? new SupabaseSettings();

supabaseSettings.Validate();

builder.Services.AddSupabaseClient(supabaseSettings);
builder.Services.AddMoaMatInfrastructure();
builder.Services.AddMoaMatAuthorization();

var host = builder.Build();

// Restore any stored session. A network failure at start-up must not stop the
// application from loading: the user simply lands on the sign-in screen.
try
{
    await host.Services.GetRequiredService<Supabase.Client>().InitializeAsync();
}
catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
{
    host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Supabase")
        .LogWarning(exception, "Supabase could not be initialised at start-up.");
}

await host.RunAsync();
