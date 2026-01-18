using Blazored.LocalStorage;
using ChefKnifeStudios.PokerAttack.Client.Core;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Client.Core.Services.EndpointServices;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services;
using ChefKnifeStudios.PokerAttack.Client.Shared.Services.JsInterop;
using ChefKnifeStudios.PokerAttack.Client.Shared.ViewModels;
using ChefKnifeStudios.PokerAttack.Client.WebApp;
using ChefKnifeStudios.PokerAttack.Shared;
using MatBlazor;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

#region REGISTER SERVICES
// Bind and register AppSettings
var appSettings = new AppSettings();
builder.Configuration.GetSection("AppSettings").Bind(appSettings);
builder.Services.AddSingleton(appSettings);

// Setup HttpClients
foreach (var api in appSettings.ExternalApis.Where(a => a.AddHttpClient))
{
    if (string.IsNullOrWhiteSpace(api.Name) || string.IsNullOrWhiteSpace(api.BaseUri))
        continue;

    if (!Uri.TryCreate(api.BaseUri, UriKind.Absolute, out var parsedUri))
        continue;

    builder.Services.AddHttpClient(api.Name, c =>
    {
        c.BaseAddress = parsedUri;
    });
}

builder.Services.AddSingleton<IHttpServiceFactory>(sp =>
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    return new HttpServiceFactory(name => clientFactory.CreateClient(name));
});

builder.Services.AddSingleton<IFeatureFlagService>(sp =>
    new FeatureFlagService(appSettings.FeatureFlags));

// Register JS Interop Services
builder.Services.AddSingleton<IAudioJsInterop, AudioJsInterop>();
builder.Services.AddSingleton<ICommonJsInterop, CommonJsInterop>();
builder.Services.AddSingleton<IInputJsInterop, InputJsInterop>();
builder.Services.AddSingleton<ILobbyJsInterop, LobbyJsInterop>();
builder.Services.AddSingleton<IRecorderInterop, RecorderInterop>();

// Register Scoped Services
builder.Services.AddScoped<IInputService, InputService>();
builder.Services.AddScoped<IEventNotificationService, EventNotificationService>();
builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();
builder.Services.AddScoped<ICardImageService, CardImageService>();

// Register Transient Services
builder.Services.AddTransient<IToastService, ToastService>();
builder.Services.AddTransient<ISettingsService, SettingsService>();
builder.Services.AddTransient<IAudioService, AudioService>();
builder.Services.AddTransient<ITestEndpointsService, TestEndpointsService>();
builder.Services.AddTransient<ILobbyEndpointsService, LobbyEndpointsService>();
builder.Services.AddTransient<IGameplayEndpointsService, GameplayEndpointsService>();
builder.Services.AddTransient<IPlayerPowerEndpointsService, PlayerPowerEndpointsService>();
builder.Services.AddTransient<IScoringRulesEndpointsService, ScoringRulesEndpointsService>();
builder.Services.AddTransient<IShopEndpointsService, ShopEndpointsService>();
builder.Services.AddTransient<IUploadEndpointsService, UploadEndpointsService>();
builder.Services.AddTransient<ISoloGameplayEndpointsService, SoloGameplayEndpointsService>();
builder.Services.AddTransient<ISettingsEndpointsService, SettingsEndpointsService>();

builder.Services.AddMatBlazor();
builder.Services.AddMatToaster(config =>
{
    config.Position = MatToastPosition.BottomRight;
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
builder.Services.AddBlazoredLocalStorage();
#endregion

#region REGISTER VIEWMODELS
builder.Services.AddScoped<IApplicationViewModel, ApplicationViewModel>();
builder.Services.AddTransient<IGameplayViewModel, GameplayViewModel>();
builder.Services.AddTransient<IGameStateMachineViewModel, GameStateMachineViewModel>();
builder.Services.AddTransient<ILobbyViewModel, LobbyViewModel>();
builder.Services.AddTransient<IPlayerViewModel, PlayerViewModel>();
builder.Services.AddTransient<IScoringRulesViewModel, ScoringRulesViewModel>();
builder.Services.AddTransient<IShopViewModel, ShopViewModel>();
builder.Services.AddTransient<IPlayerPowerSelectionViewModel, PlayerPowerSelectionViewModel>();
builder.Services.AddScoped<IGameDataStore, GameDataStore>();
builder.Services.AddScoped<IGameDataService, GameDataService>();
builder.Services.AddScoped<ISoloGameplayViewModel, SoloGameplayViewModel>();
#endregion

builder.UseSentry(options =>
{
    var sentryConfig = builder.Configuration.GetSection("Sentry");
    options.Dsn = sentryConfig.GetValue<string>("Dsn");
    options.TracesSampleRate = 1.0;
    options.CaptureFailedRequests = true;
    options.Debug = true; // Enable debug output to browser console
    options.DiagnosticLevel = SentryLevel.Debug; // Show diagnostic info
    options.EnableLogs = true;
});

// Add Sentry to the logging pipeline
builder.Logging.AddConfiguration(builder.Configuration).AddSentry();

await builder.Build().RunAsync();
