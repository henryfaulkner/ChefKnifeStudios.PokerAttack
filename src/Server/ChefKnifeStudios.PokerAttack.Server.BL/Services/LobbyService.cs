using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface ILobbyService
{
    Task<Result<LobbyDTO>> CreateLobbyAsync(PlayerDTO hostPlayer, CancellationToken cancellationToken = default);
    Task<Result<LobbyDTO>> GetLobbyAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<LobbyDTO>>> GetLobbiesAsync(CancellationToken cancellationToken = default);
    Task<Result> JoinLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task<Result> LeaveLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default);
    Task<Result> LeaveLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<PlayerDTO>>> ShutDownLobbyAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<PlayerDTO>>> GetPlayersAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task<Result> UpdatePlayerAsync(PlayerDTO player, CancellationToken cancellationToken = default);
    Task<Result<string>> StartGameAsync(string lobbyId, GameModes gameMode, CancellationToken cancellationToken = default);
}

public class LobbyService(
    IKeyValueRepository<Lobby> lobbyRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IRepository<Game> gameRepository,
    IKeyValueRepository<GameStates> gameStateRepository,
    IKeyValueRepository<GameModes> gameModeRepository,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository,
    ILogger<LobbyService> logger) : ILobbyService
{
    const int NumCardsInHand = 8;

    public async Task<Result<LobbyDTO>> CreateLobbyAsync(PlayerDTO hostPlayer, CancellationToken cancellationToken = default)
    {
        // Step 1: Ensure the host isn't already in another lobby
        var removeResult = await RemovePlayerFromAllLobbiesAsync(hostPlayer.Id, cancellationToken);
        if (!removeResult.IsSuccess)
            return Result.Error("Failed to remove player from existing lobbies.");

        // Step 2: Generate unique game ID
        var lobbyId = GenerateLobbyId();

        var existingLobbyResult = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (existingLobbyResult.IsSuccess && existingLobbyResult.Value != null)
        {
            logger.LogWarning("Lobby creation failed: Reason=LobbyAlreadyExists, LobbyId={LobbyId}", lobbyId);
            return Result.Conflict("Lobby already exists.");
        }

        var hostPlayerModel = hostPlayer.MapToModel();

        // Step 3: Create the lobby with the host as the first player
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostPlayer = hostPlayerModel,
            Players = new() { hostPlayerModel },
        };

        var addResult = await lobbyRepository.AddAsync(lobbyId, lobby, cancellationToken);
        if (!addResult.IsSuccess)
            return Result.Error("Failed to create lobby.");

        var result = lobby.MapToDTO();

        logger.LogInformation(
            "Lobby created: LobbyId={LobbyId}, HostPlayerId={HostPlayerId}, HostPlayerName={HostPlayerName}",
            lobbyId, hostPlayer.Id, hostPlayer.Name);

        await notificationHelper.JoinLobbyGroupForUserAsync(hostPlayer.Id, lobbyId, cancellationToken);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.LobbyCreated,
                JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = result }, JsonOptions.Get())),
            cancellationToken
        );

        return Result.Success(result);
    }


    public async Task<Result<LobbyDTO>> GetLobbyAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var lobbyResult = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (!lobbyResult.IsSuccess || lobbyResult.Value == null)
            return Result.NotFound("Lobby not found.");

        return Result.Success(lobbyResult.Value.MapToDTO());
    }

    public async Task<Result<IEnumerable<LobbyDTO>>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        var lobbiesResult = await lobbyRepository.GetAllAsync(cancellationToken);
        if (!lobbiesResult.IsSuccess)
            return Result.Error("Failed to retrieve lobbies.");

        return Result.Success(lobbiesResult.Value.Select(kvp => kvp.Value.MapToDTO()));
    }

    public async Task<Result> JoinLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        // Ensure target lobby exists
        var targetLobbyResult = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (!targetLobbyResult.IsSuccess || targetLobbyResult.Value == null)
        {
            logger.LogWarning("Join lobby failed: LobbyId={LobbyId}, PlayerId={PlayerId}, Reason=LobbyNotFound",
                lobbyId, player.Id);
            return Result.NotFound("Lobby not found.");
        }

        var targetLobby = targetLobbyResult.Value;

        // Step 1: Ensure player is not in any other lobby
        var removeResult = await RemovePlayerFromAllLobbiesAsync(player.Id, cancellationToken);
        if (!removeResult.IsSuccess)
            return Result.Error("Failed to remove player from existing lobbies.");

        // Step 2: Add player to the new lobby
        lock (targetLobby.Players)
        {
            if (targetLobby.Players.Select(x => x.Id).Contains(player.Id))
            {
                logger.LogWarning("Join lobby failed: LobbyId={LobbyId}, PlayerId={PlayerId}, Reason=PlayerAlreadyInLobby",
                    lobbyId, player.Id);
                return Result.Conflict("Player already in the lobby.");
            }

            targetLobby.Players.Add(player.MapToModel());
        }

        var updateResult = await lobbyRepository.UpdateAsync(lobbyId, targetLobby, cancellationToken);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to join lobby.");

        logger.LogInformation(
            "Player joined lobby: LobbyId={LobbyId}, PlayerId={PlayerId}, PlayerName={PlayerName}, TotalPlayers={TotalPlayers}",
            lobbyId, player.Id, player.Name, targetLobby.Players.Count);

        await notificationHelper.JoinLobbyGroupForUserAsync(player.Id, lobbyId, cancellationToken);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.PlayerJoined,
                JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = targetLobby.MapToDTO() }, JsonOptions.Get())),
            cancellationToken
        );

        return Result.Success();
    }

    public async Task<Result> LeaveLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var lobbiesResult = await lobbyRepository.GetAllAsync(cancellationToken);
        if (!lobbiesResult.IsSuccess)
            return Result.Error("Failed to retrieve lobbies.");

        KeyValuePair<string, Lobby>? lobbyKvp = null;
        foreach (var kvp in lobbiesResult.Value)
        {
            if (kvp.Value.Players.Select(x => x.Id).Contains(player.Id))
            {
                lobbyKvp = kvp;
                break;
            }
        }
        if (!lobbyKvp.HasValue || lobbyKvp.Value.Value.HostPlayer is null)
            return Result.Success();

        if (lobbyKvp.Value.Value.HostPlayer.Id == player.Id)
        {
            // Host leaving shuts down lobby
            var shutdownResult = await ShutDownLobbyAsync(lobbyKvp.Value.Key, cancellationToken);
            if (!shutdownResult.IsSuccess)
                return shutdownResult;
            return Result.Success();
        }
        else
        {
            lock (lobbyKvp.Value.Value.Players)
            {
                lobbyKvp.Value.Value.Players.RemoveWhere(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase));
            }

            var updateResult = await lobbyRepository.UpdateAsync(lobbyKvp.Value.Key, lobbyKvp.Value.Value, cancellationToken);
            if (!updateResult.IsSuccess)
                return Result.Error("Failed to update lobby.");

            await notificationHelper.LeaveLobbyGroupForUserAsync(player.Id, lobbyKvp.Value.Key, cancellationToken);

            await notificationHelper.BroadcastToAllAsync(
                new PokerAttackNotification(
                    PokerAttackNotificationType.PlayerLeft,
                    JsonSerializer.Serialize(
                        new LobbyEventArgs()
                        {
                            Lobby = lobbyKvp.Value.Value.MapToDTO()
                        }, JsonOptions.Get()
                    )
                ),
                cancellationToken
            );

            return Result.Success();
        }
    }

    public async Task<Result> LeaveLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var lobbyResult = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (!lobbyResult.IsSuccess || lobbyResult.Value is null || lobbyResult.Value.HostPlayer is null)
            return Result.NotFound("Lobby not found.");

        var lobby = lobbyResult.Value;

        if (lobby.HostPlayer.Id == player.Id)
        {
            // Host leaving shuts down lobby
            var shutdownResult = await ShutDownLobbyAsync(lobbyId, cancellationToken);
            if (!shutdownResult.IsSuccess)
                return shutdownResult;

            await notificationHelper.BroadcastToAllAsync(
                new PokerAttackNotification(
                    PokerAttackNotificationType.LobbyShutdown,
                    JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = new LobbyDTO() { Id = lobbyId, HostPlayer = lobby.HostPlayer.MapToDTO(), } }, JsonOptions.Get())),
                cancellationToken
            );
        }
        else
        {
            lock (lobby.Players)
            {
                lobby.Players.RemoveWhere(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase));
            }

            var updateResult = await lobbyRepository.UpdateAsync(lobbyId, lobby, cancellationToken);
            if (!updateResult.IsSuccess)
                return Result.Error("Failed to update lobby.");

            await notificationHelper.BroadcastToAllAsync(
                new PokerAttackNotification(
                    PokerAttackNotificationType.PlayerLeft,
                    JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = lobby.MapToDTO() }, JsonOptions.Get())),
                cancellationToken
            );
        }

        return Result.Success();
    }

    public async Task<Result<IEnumerable<PlayerDTO>>> ShutDownLobbyAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var lobbyResult = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (!lobbyResult.IsSuccess || lobbyResult.Value is null)
            return Result.Success(Enumerable.Empty<PlayerDTO>());

        var lobby = lobbyResult.Value;

        var deleteResult = await lobbyRepository.DeleteAsync(lobbyId, cancellationToken);
        if (!deleteResult.IsSuccess)
            return Result.Error("Failed to delete lobby.");

        logger.LogInformation(
            "Lobby shutdown: LobbyId={LobbyId}, Reason=HostLeft",
            lobbyId);

        var players = lobby.Players.ToList();

        foreach (var player in players)
            await notificationHelper.LeaveLobbyGroupForUserAsync(player.Id, lobbyId, cancellationToken);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.LobbyShutdown,
                JsonSerializer.Serialize(
                    new LobbyEventArgs()
                    {
                        Lobby = new LobbyDTO()
                        {
                            Id = lobbyId,
                            HostPlayer = lobby.HostPlayer.MapToDTO(),
                        }
                    }, JsonOptions.Get()
                )
            ),
            cancellationToken
        );

        return Result.Success(players.Select(x => x.MapToDTO()));
    }

    public async Task<Result<IEnumerable<PlayerDTO>>> GetPlayersAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var lobbyResult = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (!lobbyResult.IsSuccess || lobbyResult.Value is null)
            return Result.Success(Enumerable.Empty<PlayerDTO>());

        return Result.Success(lobbyResult.Value.Players.Select(x => x.MapToDTO()));
    }

    public async Task<Result> UpdatePlayerAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        // 1. Find the lobby containing the player
        var lobbiesResult = await lobbyRepository.GetAllAsync(cancellationToken);
        if (!lobbiesResult.IsSuccess)
            return Result.Error("Failed to retrieve lobbies.");

        KeyValuePair<string, Lobby>? lobbyKvp = null;
        foreach (var kvp in lobbiesResult.Value)
        {
            if (kvp.Value.Players.Any(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase)))
            {
                lobbyKvp = kvp;
                break;
            }
        }
        if (!lobbyKvp.HasValue)
            return Result.Success();

        var lobby = lobbyKvp.Value.Value;
        var lobbyId = lobbyKvp.Value.Key;

        // 2. Update the player in the Players collection
        lock (lobby.Players)
        {
            var existingPlayer = lobby.Players.FirstOrDefault(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase));
            if (existingPlayer == null)
                return Result.NotFound("Player not found in the lobby.");

            lobby.Players.Remove(existingPlayer);
            lobby.Players.Add(player.MapToModel());
        }

        // 3. If the player is the host, update HostPlayer as well
        if (lobby.HostPlayer.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase))
        {
            lobby.HostPlayer = player.MapToModel();
        }

        // 4. Update the lobby in the repository
        var updateResult = await lobbyRepository.UpdateAsync(lobbyId, lobby, cancellationToken);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update lobby.");

        // 5. Optionally, broadcast a notification
        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.PlayerUpdated,
                JsonSerializer.Serialize(
                    new LobbyEventArgs
                    {
                        Lobby = lobby.MapToDTO()
                    },
                    JsonOptions.Get()
                )
            ),
            cancellationToken
        );

        return Result.Success();
    }

    public async Task<Result<string>> StartGameAsync(string lobbyId, GameModes gameMode, CancellationToken cancellationToken = default)
    {
        var lobbyResult = await GetLobbyAsync(lobbyId, cancellationToken);
        if (!lobbyResult.IsSuccess || lobbyResult.Value is null)
            return Result.NotFound($"Lobby not found: {lobbyId}");

        var lobbyDTO = lobbyResult.Value;

        // Validate lobby state - ensure no active game is already in progress
        if (!string.IsNullOrEmpty(lobbyDTO.GameId))
        {
            // Check if the game is still active
            var existingGameResult = await activeGameRepository.GetAsync(lobbyDTO.GameId, cancellationToken);
            if (existingGameResult.IsSuccess && existingGameResult.Value != null)
            {
                logger.LogWarning("Start game failed: LobbyId={LobbyId}, Reason=LobbyHasActiveGame, GameId={GameId}",
                    lobbyId, lobbyDTO.GameId);
                return Result.Conflict($"Lobby already has an active game: {lobbyDTO.GameId}");
            }

            // Game was cleaned up but lobby wasn't updated - fix the state
            lobbyDTO.GameId = null;
            var fixUpdateResult = await lobbyRepository.UpdateAsync(lobbyId, lobbyDTO.MapToModel(), cancellationToken);
            if (!fixUpdateResult.IsSuccess)
                return Result.Error("Failed to update lobby state.");
        }

        string activeGameId = Guid.NewGuid().ToString();
        lobbyDTO.GameId = activeGameId;

        var updateResult = await lobbyRepository.UpdateAsync(lobbyId, lobbyDTO.MapToModel(), cancellationToken);
        if (!updateResult.IsSuccess)
            return Result.Error("Failed to update lobby with game ID.");

        await gameRepository.AddAsync(
            new Game
            {
                ClientId = activeGameId,
                HostPlayerClientId = lobbyDTO.HostPlayer.Id,
            },
            cancellationToken
        );

        var activeGame = new ActiveGame
        {
            Id = activeGameId,
            Players = lobbyDTO.Players.Select(x => x.MapToModel()).ToHashSet(),
            RoundNumber = 0,
        };

        var addActiveGameResult = await activeGameRepository.AddAsync(activeGameId, activeGame, cancellationToken);
        if (!addActiveGameResult.IsSuccess)
            return Result.Error("Failed to create active game.");

        var addStateResult = await gameStateRepository.AddAsync(activeGameId, GameStates.Freebie, cancellationToken);
        if (!addStateResult.IsSuccess)
            return Result.Error("Failed to set game state.");

        var addModeResult = await gameModeRepository.AddAsync(activeGameId, gameMode, cancellationToken);
        if (!addModeResult.IsSuccess)
            return Result.Error("Failed to set game mode.");

        logger.LogInformation(
            "Game started: LobbyId={LobbyId}, GameId={GameId}, GameMode={GameMode}, PlayerCount={PlayerCount}",
            lobbyId, activeGameId, gameMode, lobbyDTO.Players.Count);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.LobbiesChanged,
                JsonSerializer.Serialize(
                    new LobbyEventArgs
                    {
                        Lobby = lobbyDTO,
                    },
                    JsonOptions.Get()
                )
            ),
            cancellationToken
        );

        await notificationHelper.BroadcastToLobbyAsync(
            lobbyId,
            new PokerAttackNotification(
                PokerAttackNotificationType.GameStarted,
                JsonSerializer.Serialize(
                    new GameStartedEventArgs
                    {
                        GameId = activeGameId,
                    },
                    JsonOptions.Get()
                )
            ),
            cancellationToken
        );

        var lobbyActiveGameResult = await activeGameRepository.GetAsync(activeGameId, cancellationToken);
        if (!lobbyActiveGameResult.IsSuccess || lobbyActiveGameResult.Value is null)
            return Result.NotFound("Game not found after creation.");

        var lobby = lobbyActiveGameResult.Value;

        foreach (var player in lobby.Players)
        {
            var deck = new Deck();
            deck.RandomizeDeck();
            var gamePlayer = new GamePlayer
            {
                Deck = deck,
                Score = 0,
                PowerPoints = 0,
            };
            int numCardsToAdd = NumCardsInHand - gamePlayer.CardsInHand.Count();
            for (int i = 0; i < numCardsToAdd; i++)
                gamePlayer.CardsInHand.Add(deck.PullCard());

            var addPlayerResult = await gamePlayerRepository.AddAsync(player.Id, gamePlayer, cancellationToken);
            if (!addPlayerResult.IsSuccess)
                return Result.Error($"Failed to create game player for {player.Id}.");

            await notificationHelper.JoinGameGroupForUserAsync(player.Id, activeGameId, cancellationToken);
        }

        return Result.Success(activeGameId);
    }

    static string GenerateLobbyId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var data = new byte[6];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(data);

        var result = new StringBuilder(6);
        foreach (var b in data)
        {
            result.Append(chars[b % chars.Length]);
        }

        return result.ToString();
    }

    async Task<Result> RemovePlayerFromAllLobbiesAsync(string playerId, CancellationToken cancellationToken)
    {
        var allLobbiesResult = await lobbyRepository.GetAllAsync(cancellationToken);
        if (!allLobbiesResult.IsSuccess)
            return Result.Error("Failed to retrieve lobbies.");

        foreach (var kvp in allLobbiesResult.Value)
        {
            var lobby = kvp.Value;
            if (!lobby.Players.Select(x => x.Id).Contains(playerId))
                continue;

            if (lobby.HostPlayer.Id == playerId)
            {
                // Shut down the old lobby if they were host
                var deleteResult = await lobbyRepository.DeleteAsync(kvp.Key, cancellationToken);
                if (!deleteResult.IsSuccess)
                    return Result.Error($"Failed to delete lobby {kvp.Key}.");

                await notificationHelper.BroadcastToAllAsync(
                    new PokerAttackNotification(
                        PokerAttackNotificationType.LobbyShutdown,
                        JsonSerializer.Serialize(
                            new LobbyEventArgs()
                            {
                                Lobby = new LobbyDTO()
                                {
                                    Id = kvp.Key,
                                    HostPlayer = lobby.HostPlayer.MapToDTO(),
                                }
                            }, JsonOptions.Get()
                        )
                    )
                );
            }
            else
            {
                lock (lobby.Players)
                {
                    lobby.Players.RemoveWhere(x => x.Id.Equals(playerId, StringComparison.InvariantCultureIgnoreCase));
                }

                var updateResult = await lobbyRepository.UpdateAsync(kvp.Key, lobby, cancellationToken);
                if (!updateResult.IsSuccess)
                    return Result.Error($"Failed to update lobby {kvp.Key}.");

                await notificationHelper.BroadcastToAllAsync(
                    new PokerAttackNotification(
                        PokerAttackNotificationType.PlayerLeft,
                        JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = lobby.MapToDTO() }, JsonOptions.Get()))
                );
            }
        }

        return Result.Success();
    }
}
