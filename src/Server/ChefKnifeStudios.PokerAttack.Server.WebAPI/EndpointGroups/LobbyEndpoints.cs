using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
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
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            var lobbies = await lobbyService.GetLobbiesAsync(cancellationToken);
            return Result.Success(lobbies);
        })
        .WithName(nameof(Endpoints.GetLobbies))
        .Produces<IEnumerable<LobbyDTO>?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapGet(Endpoints.GetLobby, async (
            string lobbyId,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            var lobby = await lobbyService.GetLobbyAsync(lobbyId, cancellationToken);
            return Result.Success(lobby);
        })
        .WithName(nameof(Endpoints.GetLobby))
        .Produces<LobbyDTO?>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.CreateLobby, async (
            CreateLobbyReqDTO reqBody,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) => 
        {
            var result = await lobbyService.CreateLobbyAsync(reqBody.HostPlayer, cancellationToken);
            return Result.Success(result);
        })
        .WithName(nameof(Endpoints.CreateLobby))
        .Accepts<CreateLobbyReqDTO>("application/json")
        .Produces<LobbyDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPatch(Endpoints.AddPlayer, async (
            AddPlayerReqDTO reqBody,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyService.JoinLobbyAsync(reqBody.LobbyId, reqBody.Player, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.AddPlayer))
        .Accepts<AddPlayerReqDTO>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.RemovePlayer, async (
            RemovePlayerReqDTO reqBody,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            if (string.IsNullOrWhiteSpace(reqBody.LobbyId))
            {
                await lobbyService.LeaveLobbyAsync(reqBody.Player, cancellationToken);
            }
            else
            {
                await lobbyService.LeaveLobbyAsync(reqBody.LobbyId, reqBody.Player, cancellationToken);
            }
            return Result.Success();
        })
        .WithName(nameof(Endpoints.RemovePlayer))
        .Accepts<RemovePlayerReqDTO>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPut(Endpoints.UpdatePlayer, async (
            PlayerDTO player,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyService.UpdatePlayerAsync(player, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.UpdatePlayer))
        .Accepts<PlayerDTO>("application/json")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapDelete(Endpoints.ShutdownLobby, async (
            string lobbyId,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyService.ShutDownLobbyAsync(lobbyId, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.ShutdownLobby))
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        group.MapPost(Endpoints.StartGame, async (
            StartGameDTO dto,
            ILobbyService lobbyService,
            CancellationToken cancellationToken = default) =>
        {
            await lobbyService.StartGameAsync(dto.LobbyId, dto.GameMode, cancellationToken);
            return Result.Success();
        })
        .WithName(nameof(Endpoints.StartGame))
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
