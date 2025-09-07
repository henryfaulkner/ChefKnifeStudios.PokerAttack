using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Tests;
using Microsoft.AspNetCore.Mvc;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class TestEndpoints
{
    public static IEndpointRouteBuilder MapTestEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Test")
            .WithTags("Test");

        group.MapPost(Endpoints.Test.SignalR, async (
            [FromBody] SignalRReq reqBody,
            [FromServices] IPokerAttackNotificationHelper signalRHelper,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken = default) =>
        {
            var logger = loggerFactory.CreateLogger(nameof(TestEndpoints));
            try
            {
                await signalRHelper.BroadcastToGameAsync(
                    reqBody.GameId, 
                    new PokerAttackNotification(PokerAttackNotificationType.Test, reqBody.Message),
                    cancellationToken
                );
                return Result.Success();
            }
            catch (ApplicationException ex)
            {
                logger.LogError(ex, "Exception in Test.SignalR endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.Error("An unexpected error occurred.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in Test.SignalR endpoint. TraceIdentifier: {TraceId}", context.TraceIdentifier);
                return Result.CriticalError("An unexpected error occurred.");
            }
        })
        .WithName(nameof(Endpoints.Test.SignalR))
        .Produces<IEnumerable<string>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
