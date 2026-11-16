using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Infrastructure;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SoloGameplay;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;
using Sentry;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Tests;

public class ReleaseProtocolTests
{
    [Test]
    public async Task SoloIdentityIsObservedBeforePersistenceOrAResponseCanBeLost()
    {
        var repository = new Mock<IKeyValueRepository<SoloGame>>(MockBehavior.Strict);
        (string Id, string Player)? allocated = null;
        repository.Setup(value => value.AddAsync(It.IsAny<string>(), It.IsAny<SoloGame>(), It.IsAny<CancellationToken>()))
            .Returns((string id, SoloGame game, CancellationToken token) =>
            {
                Assert.That(allocated, Is.EqualTo((id, game.PlayerId)));
                return Task.FromResult(Ardalis.Result.Result.Error("Simulated canceled persistence"));
            });
        var service = new SoloGameplayService(NullLogger<SoloGameplayService>.Instance, repository.Object, null!, null!, null!, null!);
        var result = await service.StartGameAsync(new("fixture"), default, (id, player) => allocated = (id, player));
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(allocated, Is.Not.Null, "No response is required to remember the allocated owner identity");
    }

    [Test]
    public void ExistingTelemetryCannotExportVerifierCookiesOrBearerHeaders()
    {
        var request = new SentryRequest { Cookies = "__Host-PokerAttackVerifier=fixture-secret" };
        request.Headers.Add("cOOkie", "fixture-secret");
        request.Headers.Add("authorization", "Bearer fixture-secret");
        request.Headers.Add("Set-Cookie", "fixture-secret");
        request.Headers.Add("Content-Type", "application/json");
        ReleaseTelemetry.Sanitize(request);
        Assert.That(request.Cookies, Is.Null);
        Assert.That(request.Headers.Keys, Is.EqualTo(new[] { "Content-Type" }));
    }

    static SignalRNotificationHub Hub(ISignalRNotificationClient caller, string? user = "fixture")
    {
        var context = new Mock<HubCallerContext>();
        context.SetupGet(value => value.UserIdentifier).Returns(user);
        var clients = new Mock<IHubCallerClients<ISignalRNotificationClient>>(MockBehavior.Strict);
        clients.SetupGet(value => value.Caller).Returns(caller);
        return new SignalRNotificationHub(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!)
            { Context = context.Object, Clients = clients.Object };
    }

    [TestCase("")]
    [TestCase("../target")]
    [TestCase("ABCDEF0123456789abcdef0123456789")]
    public void AcknowledgmentRejectsPayloadsAndTargets(string nonce)
    {
        var caller = new Mock<ISignalRNotificationClient>(MockBehavior.Strict);
        Assert.ThrowsAsync<HubException>(() => Hub(caller.Object).AcknowledgeDelivery(nonce));
        caller.VerifyNoOtherCalls();
    }

    [Test]
    public void AcknowledgmentRequiresAnIdentifiedCaller()
    {
        var caller = new Mock<ISignalRNotificationClient>(MockBehavior.Strict);
        Assert.ThrowsAsync<HubException>(() => Hub(caller.Object, null).AcknowledgeDelivery(new string('a', 32)));
        caller.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AcknowledgmentUsesOnlyCallerAfterAwaitedPrivateSendCompletes()
    {
        var order = new List<string>();
        var permit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var privateClient = new Mock<ISignalRNotificationClient>(MockBehavior.Strict);
        privateClient.Setup(value => value.ReceivePokerAttackNotification(It.IsAny<PokerAttackNotification>(), It.IsAny<CancellationToken>()))
            .Returns(async () => { await permit.Task; order.Add("private"); });
        var hubClients = new Mock<IHubClients<ISignalRNotificationClient>>(MockBehavior.Strict);
        hubClients.Setup(value => value.User("player-one")).Returns(privateClient.Object);
        var context = new Mock<IHubContext<SignalRNotificationHub, ISignalRNotificationClient>>();
        context.SetupGet(value => value.Clients).Returns(hubClients.Object);
        var helper = new PokerAttackNotificationHelper(context.Object, null!, NullLogger<PokerAttackNotificationHelper>.Instance);
        var caller = new Mock<ISignalRNotificationClient>(MockBehavior.Strict);
        var nonce = new string('b', 32);
        caller.Setup(value => value.DeliveryAcknowledged(nonce)).Returns(() => { order.Add("ack"); return Task.CompletedTask; });
        async Task OperationThenFence()
        {
            await helper.SendToPlayerAsync("player-one", new PokerAttackNotification(PokerAttackNotificationType.HandPlayed, "fixture"));
            await Hub(caller.Object).AcknowledgeDelivery(nonce);
        }
        var operation = OperationThenFence();
        Assert.That(operation.IsCompleted, Is.False);
        caller.VerifyNoOtherCalls();
        permit.SetResult();
        await operation;
        Assert.That(order, Is.EqualTo(new[] { "private", "ack" }));
        privateClient.Verify(value => value.ReceivePokerAttackNotification(It.IsAny<PokerAttackNotification>(), It.IsAny<CancellationToken>()), Times.Once);
        hubClients.Verify(value => value.User("player-one"), Times.Once);
        hubClients.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SoloLeaveDeletesOnlyMatchingSessionAndIsIdempotent()
    {
        var repository = new InMemoryKeyValueRepository<SoloGame>();
        var id = Guid.NewGuid().ToString();
        var playerId = Guid.NewGuid().ToString();
        var other = Guid.NewGuid().ToString();
        await repository.AddAsync(id, new SoloGame { Id = id, PlayerId = playerId, PlayerName = "fixture", Deck = new Deck() });
        await repository.AddAsync(other, new SoloGame { Id = other, PlayerId = Guid.NewGuid().ToString(), PlayerName = "other", Deck = new Deck() });
        var service = new SoloGameplayService(NullLogger<SoloGameplayService>.Instance, repository, null!, null!, null!, null!);
        Assert.That((await service.LeaveGameAsync(id, new(Guid.NewGuid().ToString()))).IsSuccess, Is.False);
        Assert.That((await repository.GetAsync(id)).Value, Is.Not.Null);
        Assert.That((await service.LeaveGameAsync(id, new(playerId))).IsSuccess, Is.True);
        Assert.That((await service.LeaveGameAsync(id, new(playerId))).IsSuccess, Is.True);
        Assert.That((await repository.GetAsync(other)).Value, Is.Not.Null);
        Assert.That((await repository.GetAllAsync()).Value.Count, Is.EqualTo(1));
        Assert.That((await service.LeaveGameAsync("*", new(playerId))).IsSuccess, Is.False);
    }
}
