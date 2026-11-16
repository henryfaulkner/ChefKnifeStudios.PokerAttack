using System.Collections.Concurrent;
using ChefKnifeStudios.PokerAttack.Client.Core.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Tests;

[TestFixture, NonParallelizable]
public class ClientRecoveryTests
{
    [Test]
    public async Task FailedInitialConnectionOffersReload()
    {
        using var service = Client("http://127.0.0.1:1");
        await service.InitAsync("fixture-player");
        Assert.That(service.ConnectionStatus, Is.EqualTo(ConnectionRecoveryState.Disconnected));
        Assert.That(service.RequiresReload, Is.True);
    }

    [Test]
    public async Task ReconnectedTransportDoesNotResumeOldGameplay()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RecoveryTestState>();
        await using var app = builder.Build();
        app.MapHub<RecoveryTestHub>("/cks-notification");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var service = Client(address);
        var observed = new ConcurrentQueue<ConnectionRecoveryState>();
        service.ConnectionStateChanged += () => observed.Enqueue(service.ConnectionStatus);
        await service.InitAsync("fixture&player");
        Assert.That(service.ConnectionStatus, Is.EqualTo(ConnectionRecoveryState.Connected));
        Assert.That(service.RequiresReload, Is.False);
        var state = app.Services.GetRequiredService<RecoveryTestState>();
        await service.PlayHandAsync("fixture&player", []);
        Assert.That(state.HandCalls, Is.EqualTo(1));
        Assert.That(state.PlayerIds.Single(), Is.EqualTo("fixture&player"));
        Assert.That(state.Connections.TryPeek(out var first), Is.True);
        first!.GetHttpContext()!.Abort(); // Real transport failure on a private ephemeral Kestrel listener.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((state.Connections.Count < 2 || service.ConnectionStatus != ConnectionRecoveryState.ReloadRequired) && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        Assert.That(observed, Does.Contain(ConnectionRecoveryState.Reconnecting));
        Assert.That(service.ConnectionStatus, Is.EqualTo(ConnectionRecoveryState.ReloadRequired));
        Assert.That(service.RequiresReload, Is.True);
        await service.PlayHandAsync("fixture&player", []);
        Assert.That(state.HandCalls, Is.EqualTo(1), "Old game actions must remain blocked after transport reconnection.");
    }

    static SignalRNotificationService Client(string address)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AppSettings:ExternalApis:0:Name"] = "PokerAttackSignalR",
            ["AppSettings:ExternalApis:0:BaseUri"] = address
        }).Build();
        var environment = new Mock<IWebAssemblyHostEnvironment>();
        environment.SetupGet(value => value.BaseAddress).Returns(address);
        return new(configuration, environment.Object, NullLogger<SignalRNotificationService>.Instance);
    }
}

public sealed class RecoveryTestState
{
    public ConcurrentQueue<HubCallerContext> Connections { get; } = new();
    public ConcurrentQueue<string> PlayerIds { get; } = new();
    public int HandCalls;
}

public sealed class RecoveryTestHub(RecoveryTestState state) : Hub
{
    public override Task OnConnectedAsync()
    {
        state.PlayerIds.Enqueue(Context.GetHttpContext()!.Request.Query["playerId"].ToString());
        state.Connections.Enqueue(Context);
        return base.OnConnectedAsync();
    }
    public void PlayHand(string playerId, List<CardDTO> hand) => Interlocked.Increment(ref state.HandCalls);
}
