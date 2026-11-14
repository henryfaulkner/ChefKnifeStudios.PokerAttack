using System.Net;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;

public static class PokerAttackHosting
{
    const string BrowserPolicy = "PokerAttackBrowser";

    public static IServiceCollection AddPokerAttackHosting(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredOrigins = environment.IsDevelopment()
            ? configuration["Hosting:DevelopmentAllowedOrigins"] ?? configuration["Hosting:AllowedOrigins"]
            : configuration["Hosting:AllowedOrigins"];
        var origins = (configuredOrigins ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (origins.Any(origin => origin.Contains('*') || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https")
            || uri.GetLeftPart(UriPartial.Authority) != origin))
            throw new InvalidOperationException("Hosting:AllowedOrigins must contain exact HTTP(S) origins without paths or wildcards.");

        services.AddCors(options => options.AddPolicy(BrowserPolicy, policy =>
        {
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
        services.AddHostFiltering(options =>
        {
            options.AllowedHosts = (configuration["AllowedHosts"] ?? "localhost;127.0.0.1;[::1]")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        });
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
            options.KnownProxies.Add(IPAddress.IPv6Loopback);
        });
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database-ready", tags: ["ready"], timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    public static WebApplication UsePokerAttackHosting(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseHostFiltering();
        app.UseRouting();
        app.UseCors(BrowserPolicy);
        return app;
    }

    public static WebApplication MapPokerAttackHosting(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapGet("/_ops/status", async (HttpContext context,
            IKeyValueRepository<ActiveGame> games, IKeyValueRepository<Lobby> lobbies,
            IKeyValueRepository<SoloGame> soloGames, CancellationToken cancellationToken) =>
        {
            // Check after forwarding: requests relayed by Caddy retain their public client address.
            var address = context.Connection.RemoteIpAddress;
            if (address is null || !IPAddress.IsLoopback(address)) return Results.NotFound();
            var active = await games.GetAllAsync(cancellationToken);
            var waiting = await lobbies.GetAllAsync(cancellationToken);
            var solo = await soloGames.GetAllAsync(cancellationToken);
            if (!active.IsSuccess || !waiting.IsSuccess || !solo.IsSuccess) return Results.StatusCode(503);
            return Results.Ok(new { activeGames = active.Value.Count, lobbies = waiting.Value.Count, soloGames = solo.Value.Count });
        }).ExcludeFromDescription();
        return app;
    }

    public static bool TestEndpointsEnabled(IHostEnvironment environment, IConfiguration configuration)
    {
        if ((!environment.IsDevelopment() && !environment.IsStaging())
            || !configuration.GetValue<bool>("Testing:EnableEndpoints")) return false;
        try
        {
            var database = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("PokerAttackDB") ?? string.Empty).Database;
            return database == "pokerattack_pilot" || (database?.StartsWith("pokerattack_test_", StringComparison.Ordinal) == true
                && database.Length <= 63 && database.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
