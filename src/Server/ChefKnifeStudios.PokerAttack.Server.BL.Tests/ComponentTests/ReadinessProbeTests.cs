using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace ChefKnifeStudios.PokerAttack.Server.Tests.ComponentTests;

// Tier 1: cheap host-configuration tests that directly catch the IsDevelopment() regression
// this feature fixes (ServiceDefaults/Extensions.cs). No Postgres required — these assert
// the pipeline shape, not gameplay behaviour, so a stub connection string is fine as long as
// nothing in the request path touches the database.
[TestFixture]
public class ReadinessProbeTests
{
    const string StubConnectionString = "Host=localhost;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1;";

    static WebApplicationFactory<Program> CreateFactory(string environment)
    {
        // ConfigureAppConfiguration on the WebApplicationFactory's host builder does not
        // reliably override ConnectionStrings:PokerAttackDB — Program.cs's top-level
        // statements call WebApplication.CreateBuilder(args) and read the connection string
        // from appsettings.{ASPNETCORE_ENVIRONMENT}.json before the factory's customization
        // hook is guaranteed to have applied. This previously caused a test run to silently
        // connect to the real appsettings.Development.json database. Environment variables
        // are read directly by CreateBuilder(args) and are reliable; never revert to
        // ConfigureAppConfiguration for this without re-verifying against a real Postgres.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
        Environment.SetEnvironmentVariable("ConnectionStrings__PokerAttackDB", StubConnectionString);

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));
    }

    [TearDown]
    public void Teardown()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__PokerAttackDB", null);
    }

    [Test]
    public async Task Alive_Returns200_InProduction()
    {
        using var factory = CreateFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Alive_RequiresNoAuthentication()
    {
        using var factory = CreateFactory(Environments.Production);
        using var client = factory.CreateClient();

        // No auth header attached — an authenticated-only endpoint would 401/403 here.
        var response = await client.GetAsync("/alive");

        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Alive_ResponseBody_ExposesNoDependencyDetail()
    {
        using var factory = CreateFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/alive");
        var body = await response.Content.ReadAsStringAsync();

        // The full health-check report format includes dependency names/status; /alive must
        // not leak that shape even if empty of real dependency checks.
        var lowerBody = body.ToLowerInvariant();
        Assert.That(lowerBody, Does.Not.Contain("entries"));
        Assert.That(lowerBody, Does.Not.Contain("postgres"));
        Assert.That(lowerBody, Does.Not.Contain("npgsql"));
    }

    [Test]
    public async Task Health_IsNotMapped_InProduction()
    {
        using var factory = CreateFactory(Environments.Production);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task OpenApiAndScalarRoutes_AreNotMapped_InProduction()
    {
        using var factory = CreateFactory(Environments.Production);
        using var client = factory.CreateClient();

        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var scalarResponse = await client.GetAsync("/scalar/v1");

        Assert.That(openApiResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(scalarResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
