using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure.PlayerPowers;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

string connectionString = builder.Configuration.GetConnectionString("PokerAttackDB")!;
builder.Services.RegisterDataServices(connectionString);

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors();

// Register SignalR
builder.Services.AddSignalR(); 
builder.Services.AddSingleton<IUserIdProvider, PlayerIdProvider>();
builder.Services.AddSingleton<IPokerAttackNotificationHelper, PokerAttackNotificationHelper>();
builder.Services.AddSingleton<IPlayerConnectionTracker, PlayerConnectionTracker>();

// Register Key-Value Stores
builder.Services.AddSingleton<IKeyValueRepository<Lobby>, InMemoryKeyValueRepository<Lobby>>();
builder.Services.AddSingleton<IKeyValueRepository<ActiveGame>, InMemoryKeyValueRepository<ActiveGame>>();
builder.Services.AddSingleton<IKeyValueRepository<GamePlayer>, InMemoryKeyValueRepository<GamePlayer>>();
builder.Services.AddSingleton<IKeyValueRepository<GameStates>, InMemoryKeyValueRepository<GameStates>>();

// Register Domain Services
builder.Services.AddScoped<ILobbyService, LobbyService>();
builder.Services.AddScoped<IGameService, GameService>();
builder.Services.AddScoped<IGameStateMachineService, GameStateMachineService>();

// Register Player Powers
builder.Services.AddSingleton<IPlayerPowerRepository, PlayerPowerRepository>();
builder.Services.AddSingleton<IPlayerPowerEffectRegistry, PlayerPowerEffectRegistry>();
builder.Services.AddScoped<IPlayerPowerService, PlayerPowerService>();

// Register Eventing Service
builder.Services.AddSingleton<IEventNotificationService, EventNotificationService>();

// Ensure a singleton subscriber that can resolve scoped services when events arrive
builder.Services.AddHostedService<GameEventSubscriber>();

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
    policy.WithOrigins("https://localhost:7150", "https://www.henryfaulkner.xyz", "https://henryfaulkner.xyz")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());

app.MapHub<SignalRNotificationHub>("/cks-notification");
app.MapTestEndpoints()
   .MapLobbyEndpoints()
   .MapGameplayEndpoints()
   .MapPlayerPowerEndpoints();

app.MapDefaultEndpoints();

app.Run();