using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Lobby;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class LobbyEndpoints
{
    public static IEndpointRouteBuilder MapLobbyEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Lobby")
            .WithTags("Lobby");

        group.MapGet(Endpoints.GetLobbies, async (
            ILobbyRepository lobbyRepo,
            CancellationToken cancellationToken = default) =>
        {
            var lobbies = await lobbyRepo.GetLobbiesAsync(cancellationToken);
            return Result.Success(lobbies);
        })
        .WithName(nameof(Endpoints.GetLobbies))
        .Produces<IEnumerable<LobbyDTO>?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.GetLobby, async (
            string gameId,
            ILobbyRepository lobbyRepo,
            CancellationToken cancellationToken = default) =>
        {
            var lobby = await lobbyRepo.GetLobbyAsync(gameId, cancellationToken);
            return Result.Success(lobby);
        })
        .WithName(nameof(Endpoints.GetLobby))
        .Produces<LobbyDTO?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.CreateLobby, async (
            CreateLobbyReqDTO reqBody,
            ILobbyRepository lobbyRepo,
            CancellationToken cancellationToken = default) => 
        {
            var result = await lobbyRepo.CreateLobbyAsync(reqBody.HostPlayerId, cancellationToken);
            return Result.Success(result);
        })
        .WithName(nameof(Endpoints.CreateLobby))
        .Accepts<CreateLobbyReqDTO>("application/json")
        .Produces<LobbyDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPatch(Endpoints.AddPlayer, async (
            AddPlayerReqDTO reqBody,
            ILobbyRepository lobbyRepo,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyRepo.AddPlayerToLobbyAsync(reqBody.GameId, reqBody.PlayerId, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.AddPlayer))
        .Accepts<AddPlayerReqDTO>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPatch(Endpoints.RemovePlayer, async (
            RemovePlayerReqDTO reqBody,
            ILobbyRepository lobbyRepo,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyRepo.RemovePlayerFromLobbyAsync(reqBody.GameId, reqBody.PlayerId, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.RemovePlayer))
        .Accepts<RemovePlayerReqDTO>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapDelete(Endpoints.ShutdownLobby, async (
            string gameId,
            ILobbyRepository lobbyRepo,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyRepo.ShutDownLobbyAsync(gameId, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.ShutdownLobby))
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
