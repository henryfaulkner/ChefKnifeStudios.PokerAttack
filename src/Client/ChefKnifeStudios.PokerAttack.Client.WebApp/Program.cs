using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Client.WebApp;
using MatBlazor;
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

builder.Services.AddSingleton<IInputService, InputService>();
builder.Services.AddSingleton<IInputJsInterop, InputJsInterop>();
builder.Services.AddSingleton<ICommonJsInterop, CommonJsInterop>();
builder.Services.AddSingleton<ILobbyJsInterop, LobbyJsInterop>();
builder.Services.AddScoped<IEventNotificationService, EventNotificationService>();
builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();
builder.Services.AddScoped<ICardImageService, CardImageService>();
builder.Services.AddTransient<ITestEndpointsService, TestEndpointsService>();
builder.Services.AddTransient<ILobbyEndpointsService, LobbyEndpointsService>();
builder.Services.AddTransient<IGameplayEndpointsService, GameplayEndpointsService>();
builder.Services.AddTransient<IPlayerPowerEndpointsService, PlayerPowerEndpointsService>();
builder.Services.AddTransient<IToastService, ToastService>();

builder.Services.AddMatBlazor();
builder.Services.AddMatToaster(config =>
{
    config.Position = MatToastPosition.TopLeft;
    config.PreventDuplicates = true;
    config.NewestOnTop = true;
    config.VisibleStateDuration = 3000;
    config.ShowCloseButton = true;
    config.ShowProgressBar = true;
    config.MaximumOpacity = 100;
    config.ShowTransitionDuration = 300;
    config.VisibleStateDuration = 4000;
    config.HideTransitionDuration = 300;
    config.RequireInteraction = false;
});
#endregion

#region REGISTER VIEWMODELS
builder.Services.AddScoped<IApplicationViewModel, ApplicationViewModel>();
builder.Services.AddTransient<IGameplayViewModel, GameplayViewModel>();
builder.Services.AddTransient<IGameStateMachineViewModel, GameStateMachineViewModel>();
builder.Services.AddTransient<ILobbyViewModel, LobbyViewModel>();
builder.Services.AddTransient<IPlayerViewModel, PlayerViewModel>();
builder.Services.AddTransient<IScoreboardViewModel, ScoreboardViewModel>();
builder.Services.AddTransient<IPlayerPowerListViewModel, PlayerPowerListViewModel>();
#endregion

await builder.Build().RunAsync();
