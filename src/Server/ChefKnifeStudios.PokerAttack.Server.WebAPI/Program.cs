using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces.Repos;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure.Repos;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;
using Microsoft.AspNetCore.SignalR;
using Scalar.AspNetCore;
using StackExchange.Redis;

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
builder.Services.AddSignalR(); 
builder.Services.AddSingleton<IUserIdProvider, PlayerIdProvider>();
builder.Services.AddSingleton<IPokerAttackNotificationHelper, PokerAttackNotificationHelper>();
builder.Services.AddSingleton<ILobbyRepository, InMemoryLobbyRepository>();
builder.Services.AddSingleton<IPlayerScoreRepository, InMemoryPlayerScoreRepository>();
builder.Services.AddSingleton<IPlayerDeckRepository, InMemoryPlayerDeckRepository>();
builder.Services.AddScoped<ILobbyService, LobbyService>();
builder.Services.AddScoped<IGameService, GameService>();

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
    policy.WithOrigins("https://localhost:7150", "https://www.henryfaulkner.xyz")
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());

app.MapHub<SignalRNotificationHub>("/cks-notification");
app.MapTestEndpoints();
app.MapLobbyEndpoints();

app.MapDefaultEndpoints();

app.Run();