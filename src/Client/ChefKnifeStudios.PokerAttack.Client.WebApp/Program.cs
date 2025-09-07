using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ChefKnifeStudios.PokerAttack.Client.WebApp;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Setup HttpClients
var apis = builder.Configuration.GetSection("AppSettings:ExternalApis").GetChildren();
foreach (var item in apis)
{
    if (!item.GetValue<bool>("AddHttpClient")) continue;

    var apiName = item.GetValue<string>("Name", string.Empty);
    var baseUri = item.GetValue("BaseUri", string.Empty);

    if (string.IsNullOrWhiteSpace(apiName) || string.IsNullOrWhiteSpace(baseUri))
        continue;

    if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var parsedUri))
        continue;

    builder.Services.AddHttpClient(apiName, c =>
    {
        c.BaseAddress = parsedUri;
    });
}

builder.Services.AddSingleton<IHttpServiceFactory>(sp =>
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    return new HttpServiceFactory(name => clientFactory.CreateClient(name));
});

builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();
builder.Services.AddScoped<ITestEndpointsService, TestEndpointsService>();

await builder.Build().RunAsync();
