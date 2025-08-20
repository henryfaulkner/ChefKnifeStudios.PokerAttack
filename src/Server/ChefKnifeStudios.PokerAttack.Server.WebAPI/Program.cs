using ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddCors();
builder.Services.AddSignalR();
builder.Services.AddSingleton<PokerAttackNotificationHelper, PokerAttackNotificationHelper>();

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
    policy.AllowAnyOrigin() // temporarily allowing all origins till the final environments ready
        .AllowAnyMethod()
        .AllowAnyHeader());

app.MapHub<SignalRNotificationHub>("/cks-notification");

app.MapDefaultEndpoints();

app.Run();