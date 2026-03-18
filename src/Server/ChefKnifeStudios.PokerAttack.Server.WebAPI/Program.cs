using ChefKnifeStudios.PokerAttack.Server.BL;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

string connectionString = builder.Configuration.GetConnectionString("PokerAttackDB")!;
builder.Services.RegisterDataServices(connectionString);

// Register GameSettings from configuration
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameSettings"));

// Add services to the container.
builder.Services.AddProblemDetails();

builder.WebHost
    .UseSentry(o =>
    {
        // A DSN is required. You can set here in code, in the SENTRY_DSN environment variable or in your appsettings.json
        // See https://docs.sentry.io/product/sentry-basics/dsn-explainer/
        var sentryConfig = builder.Configuration.GetSection("Sentry");
        o.Dsn = sentryConfig.GetValue<string>("Dsn");
        o.ProfilesSampleRate = 0.1;
        o.TracesSampleRate = 1.0;
        o.EnableLogs = true;
    })
    .ConfigureLogging((c, l) =>
    {
        l.AddConfiguration(builder.Configuration);
        // Adding Sentry integration to Microsoft.Extensions.Logging
        l.AddSentry();
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    var jsonSerializationOptions = JsonOptions.Get();
    options.SerializerOptions.PropertyNameCaseInsensitive = jsonSerializationOptions.PropertyNameCaseInsensitive;
    options.SerializerOptions.DefaultIgnoreCondition = jsonSerializationOptions.DefaultIgnoreCondition;
    options.SerializerOptions.TypeInfoResolver = jsonSerializationOptions.TypeInfoResolver;
    foreach (var converter in jsonSerializationOptions.Converters) options.SerializerOptions.Converters.Add(converter);
});

// Register SignalR
builder.Services.AddSignalR(); 
builder.Services.AddSingleton<IUserIdProvider, PlayerIdProvider>();
builder.Services.AddSingleton<IPokerAttackNotificationHelper, PokerAttackNotificationHelper>();
builder.Services.AddSingleton<IPlayerConnectionTracker, PlayerConnectionTracker>();

// Register Key-Value Stores
builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));

// Register Domain Services
builder.Services.AddScoped<ILobbyService, LobbyService>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IGameStateMachineService, GameStateMachineService>();
builder.Services.AddScoped<IPlayerPowerService, PlayerPowerService>();
builder.Services.AddScoped<IShopService, ShopService>();
builder.Services.AddScoped<ISoloGameplayService, SoloGameplayService>();
builder.Services.AddSingleton<IScoringRulesService, ScoringRulesService>();
builder.Services.AddSingleton<IItemEffectsService, ItemEffectsService>();
builder.Services.AddSingleton<IWagerService, WagerService>();

// Register Player Power Singletons
builder.Services.AddSingleton<IPlayerPowerRepository, PlayerPowerRepository>();
builder.Services.AddSingleton<IPlayerPowerEffectRegistry, PlayerPowerEffectRegistry>();

// Register Item Singletons
builder.Services.AddSingleton<IItemRepository, ItemRepository>();

// Register Feature Flag Service
var featureFlags = builder.Configuration.GetSection("FeatureFlags").Get<Dictionary<FeatureFlags, bool>>() ?? new();
builder.Services.AddSingleton<IFeatureFlagService>(new FeatureFlagService(featureFlags));

// Register Eventing Service
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();
builder.Services.AddHostedService<EventNotificationServiceSubscriber>();

// Register Cleanup Background Service
builder.Services.AddHostedService<CleanupBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.MapOpenApi()
    .AllowAnonymous();

app.MapScalarApiReference(options =>
{
    options.HiddenClients = true;
    options
        .WithTitle("PokerAttack API")
        .WithDocumentDownloadType(DocumentDownloadType.Both)
        .WithTheme(ScalarTheme.Solarized)
        .WithLayout(ScalarLayout.Classic)
        .WithClientButton(false)
        .WithDarkMode(true)
        .WithDefaultHttpClient(ScalarTarget.JavaScript, ScalarClient.Axios);
}).AllowAnonymous();

app.UseCors(policy =>
    policy.WithOrigins(
            "https://localhost:7150",
            "http://localhost:5186",
            "https://localhost:7333", 
            "https://www.henryfaulkner.xyz",
            "https://henryfaulkner.xyz")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());

app.MapHub<SignalRNotificationHub>("/cks-notification");
app.MapTestEndpoints()
   .MapLobbyEndpoints()
   .MapGameplayEndpoints()
   .MapPlayerPowerEndpoints()
   .MapShopEndpoints()
   .MapScoringRulesEndpoints()
   .MapSoloGameplayEndpoints()
   .MapSettingsEndpoints();

app.MapDefaultEndpoints();

app.Run();