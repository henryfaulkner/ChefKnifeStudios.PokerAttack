using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http.Features;
using Moq;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Tests;

public class ReleaseAdmissionTests
{
    string state = null!;

    [SetUp]
    public void SetUp() => state = Path.Combine(Path.GetTempPath(), "pokerattack-admission-" + Guid.NewGuid().ToString("N") + ".json");

    [TearDown]
    public void TearDown() => File.Delete(state);

    ReleaseAdmission Admission() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Hosting:MaintenanceStatePath"] = state,
            ["Hosting:ReleaseImage"] = "chefknife.azurecr.io/chefknifestudios.pokerattack.server.webapi@sha256:" + new string('b', 64),
            ["Hosting:ReleaseConfigSha256"] = new string('c', 64),
            ["POKERATTACK_SOURCE_REVISION"] = new string('d', 40) }).Build());

    void VerifierState()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        File.WriteAllText(state, JsonSerializer.Serialize(new { active = true, run_id = 123, run_attempt = 1,
            digest = "sha256:" + new string('b', 64), config_sha256 = new string('c', 64), source_sha = new string('d', 40),
            verifier = new { id = new string('f', 32), token_sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(new string('e', 64)))),
                issued_unix = now - 5, expires_unix = now + 900, revoked = false,
                players = new[] { new { id = new string('a', 32), name = "Release-123-1-0" },
                    new { id = new string('b', 32), name = "Release-123-1-1" } } } }));
    }

    static DefaultHttpContext VerifierContext(string path = "/lobby/create-lobby", string? body = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Path = path;
        context.Request.Headers.Authorization = "Bearer 123.1." + new string('e', 64);
        body ??= JsonSerializer.Serialize(new { hostPlayer = new { id = new string('a', 32), name = "Release-123-1-0" } });
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return context;
    }

    [Test]
    public async Task OwnedRecoveryReturnsOnlyAllocatedIdsAndNoGlobalEnumerationOrPrivateData()
    {
        VerifierState();
        var admission = Admission();
        var context = VerifierContext();
        var lobbies = new Mock<IKeyValueRepository<Lobby>>(MockBehavior.Strict);
        var games = new Mock<IKeyValueRepository<ActiveGame>>(MockBehavior.Strict);
        var solo = new Mock<IKeyValueRepository<SoloGame>>(MockBehavior.Strict);
        var player = new Player { Id = new string('a', 32), Name = "PRIVATE-STORED-NAME" };
        lobbies.Setup(repository => repository.GetAsync("ABC123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ardalis.Result.Result.Success<Lobby?>(new() { Id = "ABC123", HostPlayer = player, Players = [player] }));
        var gameId = Guid.NewGuid().ToString();
        games.Setup(repository => repository.GetAsync(gameId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ardalis.Result.Result.Success<ActiveGame?>(new() { Id = gameId, Players = [player] }));
        var soloId = Guid.NewGuid().ToString(); var soloPlayer = Guid.NewGuid().ToString();
        solo.Setup(repository => repository.GetAsync(soloId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ardalis.Result.Result.Success<SoloGame?>(new() { Id = soloId, PlayerId = soloPlayer,
                PlayerName = "PRIVATE-SOLO-NAME", Deck = new() }));
        context.RequestServices = new ServiceCollection().AddSingleton(lobbies.Object).AddSingleton(games.Object).AddSingleton(solo.Object).BuildServiceProvider();
        Assert.That(await admission.AllowNewSessionAsync(context), Is.True);
        admission.RememberSession(context, "lobby", "ABC123");
        admission.RememberSession(context, "game", gameId);
        admission.RememberSession(context, "solo", soloId, soloPlayer);
        var owned = await admission.OwnedSessionsAsync(context, default);
        Assert.That(owned!.Lobbies.Single().Id, Is.EqualTo("ABC123"));
        Assert.That(owned.Games, Is.EqualTo(new[] { gameId }));
        Assert.That(owned.Solo.Single().PlayerId, Is.EqualTo(soloPlayer));
        Assert.That(JsonSerializer.Serialize(owned), Does.Not.Contain("PRIVATE-"));
        lobbies.Verify(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        games.Verify(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        solo.Verify(repository => repository.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        lobbies.Verify(repository => repository.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ResumedAttemptCanRecoverOldOwnedIdsButWrongRunAndRevokedTokensCannotListThem()
    {
        VerifierState();
        var admission = Admission(); var context = VerifierContext();
        var repository = new InMemoryKeyValueRepository<Lobby>();
        context.RequestServices = new ServiceCollection().AddSingleton<IKeyValueRepository<Lobby>>(repository).BuildServiceProvider();
        Assert.That(await admission.AllowNewSessionAsync(context), Is.True);
        admission.RememberSession(context, "lobby", "ABC123");
        var host = new Player { Id = new string('a', 32), Name = "Release-123-1-0" };
        await repository.AddAsync("ABC123", new() { Id = "ABC123", HostPlayer = host, Players = [host] });
        var stateDocument = JsonNode.Parse(File.ReadAllText(state))!;
        stateDocument["run_attempt"] = 2;
        stateDocument["verifier"]!["id"] = new string('1', 32);
        stateDocument["verifier"]!["players"] = JsonSerializer.SerializeToNode(new[] {
            new { id = new string('2', 32), name = "Release-123-2-0" }, new { id = new string('3', 32), name = "Release-123-2-1" } });
        File.WriteAllText(state, stateDocument.ToJsonString());
        Assert.That(await admission.OwnedSessionsAsync(context, default), Is.Null, "Old attempt rejected");
        context.Request.Headers.Authorization = "Bearer 123.2." + new string('e', 64);
        Assert.That((await admission.OwnedSessionsAsync(context, default))!.Lobbies.Single().Id, Is.EqualTo("ABC123"));
        stateDocument["run_id"] = 124; File.WriteAllText(state, stateDocument.ToJsonString());
        context.Request.Headers.Authorization = "Bearer 124.2." + new string('e', 64);
        Assert.That((await admission.OwnedSessionsAsync(context, default))!.Lobbies, Is.Empty, "Different run cannot enumerate prior owner");
        stateDocument["verifier"]!["revoked"] = true; File.WriteAllText(state, stateDocument.ToJsonString());
        Assert.That(await admission.OwnedSessionsAsync(context, default), Is.Null);
        Assert.That((await repository.GetAsync("ABC123")).Value, Is.Not.Null, "Listing never deletes a session");
    }

    [Test]
    public async Task InFlightCreationIsNotMisreportedAsEmptyAndOldPlayerIdsCannotBeReused()
    {
        VerifierState(); var admission = Admission(); var context = VerifierContext();
        using (admission.ObserveCreation())
            Assert.That((await admission.OwnedSessionsAsync(context, default))!.InFlightOperations, Is.EqualTo(1));
        var stateDocument = JsonNode.Parse(File.ReadAllText(state))!;
        stateDocument["run_attempt"] = 2; stateDocument["verifier"]!["id"] = new string('1', 32);
        File.WriteAllText(state, stateDocument.ToJsonString());
        context.Request.Headers.Authorization = "Bearer 123.2." + new string('e', 64);
        Assert.That(await admission.OwnedSessionsAsync(context, default), Is.Null, "Never reconnect old verifier player IDs under a fresh grant");
    }

    [Test]
    public async Task VerifierAllowsOnlyRegisteredBoundedSessionsOverTls()
    {
        VerifierState();
        var admission = Admission();
        Assert.That(await admission.AllowNewSessionAsync(VerifierContext()), Is.True);
        Assert.That(await admission.AllowNewSessionAsync(VerifierContext()), Is.False, "No duplicate lobby admission");
        Assert.That(await admission.AllowNewSessionAsync(VerifierContext("/solo/start-game", "{\"playerName\":\"Release-123-1-0\"}")), Is.True);
        Assert.That(await admission.AllowNewSessionAsync(VerifierContext("/solo/start-game", "{\"playerName\":\"ordinary-user\"}")), Is.False);
        Assert.That(await admission.AllowNewSessionAsync(VerifierContext("/_ops/status")), Is.False);
        Assert.That(await admission.AllowNewSessionAsync(VerifierContext("/test/cleanup")), Is.False);
        Assert.That(await Admission().AllowNewSessionAsync(VerifierContext(body: "{\"hostPlayer\":{\"id\":\"ordinary\",\"name\":\"Release-123-1-0\"}}")), Is.False);
    }

    [TestCase("wrong-run")]
    [TestCase("wrong-attempt")]
    [TestCase("wrong-secret")]
    [TestCase("http")]
    [TestCase("wrong-source")]
    [TestCase("wrong-digest")]
    [TestCase("wrong-config")]
    [TestCase("expired")]
    [TestCase("future")]
    [TestCase("overlong")]
    [TestCase("revoked")]
    public async Task VerifierRejectsMismatchedExpiredAndInsecureCredentials(string fault)
    {
        VerifierState();
        var context = VerifierContext();
        var document = JsonNode.Parse(File.ReadAllText(state))!;
        switch (fault)
        {
            case "wrong-run": context.Request.Headers.Authorization = "Bearer 124.1." + new string('e', 64); break;
            case "wrong-attempt": context.Request.Headers.Authorization = "Bearer 123.2." + new string('e', 64); break;
            case "wrong-secret": context.Request.Headers.Authorization = "Bearer 123.1." + new string('0', 64); break;
            case "http": context.Request.Scheme = "http"; break;
            case "wrong-source": document["source_sha"] = new string('0', 40); break;
            case "wrong-digest": document["digest"] = "sha256:" + new string('0', 64); break;
            case "wrong-config": document["config_sha256"] = new string('0', 64); break;
            case "expired": document["verifier"]!["expires_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); break;
            case "future": document["verifier"]!["issued_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60; break;
            case "overlong": document["verifier"]!["expires_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; break;
            case "revoked": document["verifier"]!["revoked"] = true; break;
        }
        File.WriteAllText(state, document.ToJsonString());
        Assert.That(await Admission().AllowNewSessionAsync(context), Is.False);
    }

    [Test]
    public async Task VerifierCookieAndConnectedInvocationRecheckRevocation()
    {
        VerifierState();
        var context = VerifierContext();
        context.Request.Headers.Authorization = "";
        context.Request.Headers.Cookie = ReleaseAdmission.VerifierCookie + "=123.1." + new string('e', 64);
        var admission = Admission();
        Assert.That(await admission.CanConnectAsync(new string('a', 32), Mock.Of<IServiceProvider>(), default, context), Is.True);
        Assert.That(admission.CanInvoke(new string('a', 32), context), Is.True);
        var document = JsonNode.Parse(File.ReadAllText(state))!;
        document["verifier"]!["revoked"] = true;
        File.WriteAllText(state, document.ToJsonString());
        Assert.That(admission.CanInvoke(new string('a', 32), context), Is.False);
        File.Delete(state);
        Assert.That(admission.CanInvoke(new string('a', 32), context), Is.False, "Missing state must not forget existing verifier identity");
    }

    [TestCase("{\"active\":true}", true)]
    [TestCase("{\"active\":false}", false)]
    [TestCase("{}", true)]
    [TestCase("{\"active\":\"false\"}", true)]
    [TestCase("not-json", true)]
    public void AdmissionReadsFreshStateAndFailsClosed(string value, bool expected)
    {
        File.WriteAllText(state, value);
        Assert.That(Admission().Active, Is.EqualTo(expected));
        File.Delete(state);
        Assert.That(Admission().Active, Is.True);
    }

    [Test]
    public async Task PreviouslyLoadedClientsCannotStartMatchesDuringMaintenance()
    {
        File.WriteAllText(state, "{\"active\":true}");
        await using var app = await CreateApp();
        using var client = app.GetTestClient();
        foreach (var (method, path) in new[] { (HttpMethod.Post, "/lobby/create-lobby"),
            (HttpMethod.Patch, "/lobby/add-player"), (HttpMethod.Post, "/lobby/start-game"),
            (HttpMethod.Post, "/solo/start-game"), (HttpMethod.Post, "/SOLO/START-GAME/") })
        {
            using var request = new HttpRequestMessage(method, path);
            using var response = await client.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable), path);
            Assert.That(response.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(30)));
        }
        Assert.That((await client.PostAsync("/existing/play", null)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var status = JsonDocument.Parse(await client.GetStringAsync("/release/status"));
        Assert.That(status.RootElement.GetProperty("maintenance").GetBoolean(), Is.True);
        // The same loaded API observes a host update without a process restart.
        File.WriteAllText(state, "{\"active\":false}");
        using var reopened = JsonDocument.Parse(await client.GetStringAsync("/release/status"));
        Assert.That(reopened.RootElement.GetProperty("maintenance").GetBoolean(), Is.False);
    }

    [Test]
    public async Task StateSurvivesApplicationReplacement()
    {
        File.WriteAllText(state, "{\"active\":true}");
        for (var incarnation = 0; incarnation < 2; incarnation++)
        {
            await using var app = await CreateApp();
            using var client = app.GetTestClient();
            Assert.That((await client.PostAsync("/lobby/create-lobby", null)).StatusCode,
                Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        }
        Assert.That(File.ReadAllText(state), Is.EqualTo("{\"active\":true}"));
    }

    [Test]
    public async Task SignalRAdmissionRejectsNewPlayersAndAllowsExistingGameToDrain()
    {
        File.WriteAllText(state, "{\"active\":true}");
        await using var app = await CreateApp();
        var admission = app.Services.GetRequiredService<ReleaseAdmission>();
        var games = app.Services.GetRequiredService<IKeyValueRepository<ActiveGame>>();
        await games.AddAsync("existing", new ActiveGame { Id = "existing", Players = [new() { Id = "current-player", Name = "fixture" }] });
        Assert.That(await admission.CanConnectAsync("new-player", app.Services, default), Is.False);
        Assert.That(await admission.CanConnectAsync("current-player", app.Services, default), Is.True);
        var caller = new Mock<HubCallerContext>();
        caller.SetupGet(value => value.Features).Returns(new FeatureCollection());
        caller.SetupGet(value => value.UserIdentifier).Returns("new-player");
        var context = new HubLifetimeContext(caller.Object, app.Services, new ProbeHub());
        var proceeded = false;
        Assert.ThrowsAsync<HubException>(() => new ReleaseAdmissionHubFilter(admission)
            .OnConnectedAsync(context, _ => { proceeded = true; return Task.CompletedTask; }));
        Assert.That(proceeded, Is.False);
    }

    async Task<WebApplication> CreateApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["AllowedHosts"] = "localhost", ["Hosting:MaintenanceStatePath"] = state });
        builder.Services.AddPokerAttackHosting(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(typeof(IKeyValueRepository<>), typeof(InMemoryKeyValueRepository<>));
        builder.Services.AddSingleton(Mock.Of<ILobbyService>());
        builder.Services.AddSingleton(Mock.Of<ISoloGameplayService>());
        builder.Services.AddProblemDetails();
        var app = builder.Build();
        app.UsePokerAttackHosting();
        app.MapPokerAttackHosting();
        app.MapLobbyEndpoints().MapSoloGameplayEndpoints();
        app.MapPost("/existing/play", () => Results.Ok());
        await app.StartAsync();
        return app;
    }

    sealed class ProbeHub : Hub;
}
