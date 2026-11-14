using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Tests;

[TestFixture, NonParallelizable]
public class HostingTests
{
    readonly string container = "pokerattack-hosting-test-" + Guid.NewGuid().ToString("N");
    string connection = null!;

    [OneTimeSetUp]
    public async Task StartDisposableDatabase()
    {
        var password = Guid.NewGuid().ToString("N");
        await Docker(["run", "--detach", "--rm", "--name", container, "--tmpfs", "/var/lib/postgresql/data",
            "--publish", "127.0.0.1::5432", "--env", "POSTGRES_PASSWORD", "--env", "POSTGRES_DB", "postgres:16"],
            new() { ["POSTGRES_PASSWORD"] = password, ["POSTGRES_DB"] = "pokerattack_test_hosting" });
        try
        {
            var port = int.Parse((await Docker(["port", container, "5432/tcp"])).Trim().Split(':')[^1]);
            connection = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1", Port = port, Database = "pokerattack_test_hosting", Username = "postgres",
                Password = password, Timeout = 2, CommandTimeout = 3, Pooling = false
            }.ConnectionString;
            var deadline = DateTime.UtcNow.AddSeconds(40);
            while (true)
            {
                try { await using var client = new NpgsqlConnection(connection); await client.OpenAsync(); break; }
                catch (NpgsqlException) when (DateTime.UtcNow < deadline) { await Task.Delay(200); }
            }
            await using var database = Database(connection);
            await database.Database.MigrateAsync();
        }
        catch
        {
            await Docker(["rm", "--force", container]);
            throw;
        }
    }

    [OneTimeTearDown]
    public async Task StopDisposableDatabase() => await Docker(["rm", "--force", container], allowFailure: true);

    [Test]
    public async Task ProductionHealthIsSafeAndDoesNotRequireDatabaseForLiveness()
    {
        var broken = new NpgsqlConnectionStringBuilder(connection) { Port = 1 }.ConnectionString;
        await using var app = await CreateApp(connectionString: broken);
        using var client = app.GetTestClient();
        Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var timer = Stopwatch.StartNew();
        var ready = await client.GetAsync("/health/ready");
        Assert.That(ready.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await ready.Content.ReadAsStringAsync(), Is.EqualTo("Unhealthy"));
        Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task ReadinessDoesNotApplyMigrationsAndRecoversAfterExplicitFixtureRepair()
    {
        await using (var admin = new NpgsqlConnection(connection))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE DATABASE pokerattack_test_empty", admin);
            await create.ExecuteNonQueryAsync();
        }
        var empty = new NpgsqlConnectionStringBuilder(connection) { Database = "pokerattack_test_empty" }.ConnectionString;
        await using var app = await CreateApp(connectionString: empty);
        using var client = app.GetTestClient();
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        await using (var probe = new NpgsqlConnection(empty))
        {
            await probe.OpenAsync();
            await using var history = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = 'PokerAttack' OR table_name = '__EFMigrationsHistory'", probe);
            Assert.That(Convert.ToInt64(await history.ExecuteScalarAsync()), Is.Zero, "Probe must not create tables/history.");
        }
        await using var database = Database(empty);
        await database.Database.MigrateAsync(); // Explicit test-only fixture preparation, never a health operation.
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE \"PokerAttack\".\"Games\" RENAME COLUMN \"HostPlayerClientId\" TO \"BrokenHost\"");
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        await database.Database.ExecuteSqlRawAsync("ALTER TABLE \"PokerAttack\".\"Games\" RENAME COLUMN \"BrokenHost\" TO \"HostPlayerClientId\"");
        Assert.That((await client.GetAsync("/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("Production", true, true, false)]
    [TestCase("prod", true, true, false)]
    [TestCase("Staging", false, true, false)]
    [TestCase("Development", false, true, false)]
    [TestCase("Staging", true, false, false)]
    [TestCase("Development", true, false, false)]
    [TestCase("Staging", true, true, true)]
    [TestCase("Development", true, true, true)]
    public async Task TestRoutesRequireExplicitIsolatedNonProduction(string environment, bool enabled, bool isolated, bool allowed)
    {
        var target = isolated ? connection : new NpgsqlConnectionStringBuilder(connection) { Database = "expensetrackerdb" }.ConnectionString;
        await using var app = await CreateApp(environment, target, enabled);
        using var client = app.GetTestClient();
        var cleanup = await client.PostAsync("/test/cleanup", null);
        var signal = await client.PostAsJsonAsync("/test/signal-r", new { gameId = "fixture", message = "fixture" });
        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        Assert.That(cleanup.StatusCode, Is.EqualTo(expected));
        Assert.That(signal.StatusCode, Is.EqualTo(expected));
    }

    [TestCase("https://playpokerattack.com", true)]
    [TestCase("https://playpokerattack.com.evil.example", false)]
    [TestCase("https://evil.example", false)]
    public async Task CorsGrantsOnlyExactConfiguredOrigins(string origin, bool allowed)
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/probe");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var response = await client.SendAsync(request);
        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.EqualTo(allowed));
        if (allowed)
        {
            Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single(), Is.EqualTo(origin));
            Assert.That(response.Headers.GetValues("Access-Control-Allow-Credentials").Single(), Is.EqualTo("true"));
        }
    }

    [Test]
    public async Task MissingCorsConfigurationGrantsNoOrigin()
    {
        await using var app = await CreateApp(origins: string.Empty);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Origin", "https://playpokerattack.com");
        using var response = await client.GetAsync("/probe");
        Assert.That(response.Headers.Contains("Access-Control-Allow-Origin"), Is.False);
    }

    [Test]
    public async Task UnknownHostIsRejected()
    {
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Host = "evil.example";
        Assert.That((await client.GetAsync("/health/live")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [TestCase("127.0.0.1", "https", "203.0.113.9")]
    [TestCase("::1", "https", "203.0.113.9")]
    [TestCase("198.51.100.8", "http", "198.51.100.8")]
    public async Task ForwardedHeadersTrustOnlyLoopbackAndOneHop(string peer, string scheme, string address)
    {
        await using var app = await CreateApp(peer: peer);
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.7, 203.0.113.9");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        using var body = JsonDocument.Parse(await client.GetStringAsync("/probe"));
        Assert.That(body.RootElement.GetProperty("scheme").GetString(), Is.EqualTo(scheme));
        Assert.That(body.RootElement.GetProperty("address").GetString(), Is.EqualTo(address));
    }

    [Test]
    public async Task OperatorStatusIsLocalOnlyAndContainsOnlyCounts()
    {
        await using var app = await CreateApp();
        await app.Services.GetRequiredService<IKeyValueRepository<ActiveGame>>().AddAsync("fixture", new() { Id = "fixture" });
        using var client = app.GetTestClient();
        using var body = JsonDocument.Parse(await client.GetStringAsync("/_ops/status"));
        Assert.That(body.RootElement.GetProperty("activeGames").GetInt32(), Is.EqualTo(1));
        Assert.That(body.RootElement.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "activeGames", "lobbies", "soloGames" }));
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.9");
        Assert.That((await client.GetAsync("/_ops/status")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UntrustedPeerCannotSpoofLoopbackForOperatorStatus()
    {
        await using var app = await CreateApp(peer: "203.0.113.9");
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.That((await client.GetAsync("/_ops/status")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task HostHasBoundedShutdownBudget()
    {
        await using var app = await CreateApp();
        Assert.That(app.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task ReadinessRejectsIncompleteStartupOrStoppingWithoutDatabaseWork(bool started, bool stopping)
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(value => value.ApplicationStarted).Returns(new CancellationToken(started));
        lifetime.SetupGet(value => value.ApplicationStopping).Returns(new CancellationToken(stopping));
        var check = new DatabaseReadinessCheck(scopes.Object, lifetime.Object);
        Assert.That((await check.CheckHealthAsync(new HealthCheckContext())).Status, Is.EqualTo(HealthStatus.Unhealthy));
        scopes.VerifyNoOtherCalls();
    }

    [TestCase("*")]
    [TestCase("https://*.playpokerattack.com")]
    [TestCase("https://playpokerattack.com/path")]
    public void InvalidOriginConfigurationFailsAtStartup(string origins)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hosting:AllowedOrigins"] = origins
        }).Build();
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(value => value.EnvironmentName).Returns("Production");
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPokerAttackHosting(configuration, environment.Object));
    }

    static AppDbContext Database(string connection) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);

    async Task<WebApplication> CreateApp(string environment = "Production", string? connectionString = null,
        bool testEndpoints = false, string peer = "127.0.0.1", string origins = "https://playpokerattack.com")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:PokerAttackDB"] = connectionString ?? connection,
            ["Hosting:AllowedOrigins"] = origins,
            ["AllowedHosts"] = "localhost;127.0.0.1;[::1];api.playpokerattack.com",
            ["Testing:EnableEndpoints"] = testEndpoints.ToString()
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.AddDefaultHealthChecks();
        builder.Services.AddProblemDetails();
        builder.Services.RegisterDataServices(connectionString ?? connection);
        builder.Services.AddPokerAttackHosting(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
        builder.Services.AddSingleton(Mock.Of<IPokerAttackNotificationHelper>());
        var app = builder.Build();
        // Test-only connection metadata, never an externally supplied request header.
        app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse(peer); return next(context); });
        app.UsePokerAttackHosting();
        app.MapPokerAttackHosting();
        app.MapTestEndpoints();
        app.MapGet("/probe", (HttpContext context) => new { scheme = context.Request.Scheme, address = context.Connection.RemoteIpAddress?.ToString() });
        await app.StartAsync();
        return app;
    }

    static async Task<string> Docker(string[] arguments, Dictionary<string, string>? environment = null, bool allowFailure = false)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = await output;
        var error = await errors;
        if (process.ExitCode != 0 && !allowFailure) throw new InvalidOperationException("Docker fixture failed: " + error);
        return result;
    }
}
