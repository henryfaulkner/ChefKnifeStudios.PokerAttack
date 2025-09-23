using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Client.WebApp;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

#region REGISTER SERVICES
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
builder.Services.AddScoped<ICardImageService, CardImageService>();
builder.Services.AddTransient<ICommonJsInterop, CommonJsInterop>();
builder.Services.AddTransient<ILobbyJsInterop, LobbyJsInterop>();
builder.Services.AddScoped<ITestEndpointsService, TestEndpointsService>();
builder.Services.AddScoped<ILobbyEndpointsService, LobbyEndpointsService>();
#endregion

#region REGISTER VIEWMODELS
builder.Services.AddScoped<IApplicationViewModel, ApplicationViewModel>();
builder.Services.AddScoped<ILobbyViewModel, LobbyViewModel>();
#endregion

await builder.Build().RunAsync();
