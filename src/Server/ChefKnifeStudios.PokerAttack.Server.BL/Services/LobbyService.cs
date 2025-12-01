using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Repos;
using ChefKnifeStudios.PokerAttack.Shared;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;
using ChefKnifeStudios.PokerAttack.Shared.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Server.BL.Services;

public interface ILobbyService
{
    Task<LobbyDTO> CreateLobbyAsync(PlayerDTO hostPlayer, CancellationToken cancellationToken = default);
    Task<LobbyDTO?> GetLobbyAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task<IEnumerable<LobbyDTO>> GetLobbiesAsync(CancellationToken cancellationToken = default);
    Task JoinLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default);
    Task<IEnumerable<PlayerDTO>> ShutDownLobbyAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PlayerDTO>> GetPlayersAsync(string lobbyId, CancellationToken cancellationToken = default);
    Task UpdatePlayerAsync(PlayerDTO player, CancellationToken cancellationToken = default);
    Task StartGameAsync(string lobbyId, CancellationToken cancellationToken = default);
}

public class LobbyService(
    IKeyValueRepository<Lobby> lobbyRepository,
    IPokerAttackNotificationHelper notificationHelper,
    IRepository<Game> gameRepository,
    IKeyValueRepository<GameStates> gameStateRepository,
    IKeyValueRepository<ActiveGame> activeGameRepository,
    IKeyValueRepository<GamePlayer> gamePlayerRepository) : ILobbyService
{
    const int NumCardsInHand = 8;

    public async Task<LobbyDTO> CreateLobbyAsync(PlayerDTO hostPlayer, CancellationToken cancellationToken = default)
    {
        // Step 1: Ensure the host isn't already in another lobby
        await RemovePlayerFromAllLobbiesAsync(hostPlayer.Id, cancellationToken);

        // Step 2: Generate unique game ID
        var lobbyId = GenerateLobbyId();

        if (await lobbyRepository.GetAsync(lobbyId, cancellationToken) is Lobby)
            throw new InvalidOperationException("Lobby already exists.");

        var hostPlayerModel = hostPlayer.MapToModel();

        // Step 3: Create the lobby with the host as the first player
        var lobby = new Lobby
        {
            Id = lobbyId,
            HostPlayer = hostPlayerModel,
            Players = new() { hostPlayerModel },
        };

        await lobbyRepository.AddAsync(lobbyId, lobby, cancellationToken);

        var result = lobby.MapToDTO();

        await notificationHelper.JoinLobbyGroupForUserAsync(hostPlayer.Id, lobbyId, cancellationToken);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.LobbyCreated,
                JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = result }, JsonOptions.Get())),
            cancellationToken
        );

        return result;
    }


    public async Task<LobbyDTO?> GetLobbyAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var lobby = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        return lobby?.MapToDTO();
    }

    public async Task<IEnumerable<LobbyDTO>> GetLobbiesAsync(CancellationToken cancellationToken = default)
    {
        var lobbies = await lobbyRepository.GetAllAsync(cancellationToken);
        return lobbies.Select(kvp => kvp.Value.MapToDTO());
    }

    public async Task JoinLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        // Ensure target lobby exists
        var targetLobby = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (targetLobby is null)
            throw new KeyNotFoundException("Lobby not found.");

        // Step 1: Ensure player is not in any other lobby
        await RemovePlayerFromAllLobbiesAsync(player.Id, cancellationToken);

        // Step 2: Add player to the new lobby
        lock (targetLobby.Players)
        {
            if (targetLobby.Players.Select(x => x.Id).Contains(player.Id))
                throw new InvalidOperationException("Player already in the lobby.");

            targetLobby.Players.Add(player.MapToModel());
        }

        await lobbyRepository.UpdateAsync(lobbyId, targetLobby, cancellationToken);

        await notificationHelper.JoinLobbyGroupForUserAsync(player.Id, lobbyId, cancellationToken);

        await notificationHelper.BroadcastToAllAsync(
            new PokerAttackNotification(
                PokerAttackNotificationType.PlayerJoined,
                JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = targetLobby.MapToDTO() }, JsonOptions.Get())),
            cancellationToken
        );
    }

    public async Task LeaveLobbyAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {

        var lobbiesKvps = await lobbyRepository.GetAllAsync(cancellationToken);
        KeyValuePair<string, Lobby>? lobbyKvp = null;
        foreach (var kvp in lobbiesKvps)
        {
            if (kvp.Value.Players.Select(x => x.Id).Contains(player.Id))
            {
                lobbyKvp = kvp; 
                break;
            }
        }
        if (!lobbyKvp.HasValue || lobbyKvp.Value.Value.HostPlayer is null) return;

        if (lobbyKvp.Value.Value.HostPlayer.Id == player.Id)
        {
            // Host leaving shuts down lobby
            await ShutDownLobbyAsync(lobbyKvp.Value.Key, cancellationToken);
        }
        else
        {
            lock (lobbyKvp.Value.Value.Players)
            {
                lobbyKvp.Value.Value.Players.RemoveWhere(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase));
            }
            await lobbyRepository.UpdateAsync(lobbyKvp.Value.Key, lobbyKvp.Value.Value, cancellationToken);

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
        }
    }

    public async Task LeaveLobbyAsync(string lobbyId, PlayerDTO player, CancellationToken cancellationToken = default)
    {
        var lobby = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (lobby is null || lobby.HostPlayer is null)
            throw new KeyNotFoundException("Lobby not found.");

        if (lobby.HostPlayer.Id == player.Id)
        {
            // Host leaving shuts down lobby
            await ShutDownLobbyAsync(lobbyId, cancellationToken);

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
            await lobbyRepository.UpdateAsync(lobbyId, lobby, cancellationToken);

            await notificationHelper.BroadcastToAllAsync(
                new PokerAttackNotification(
                    PokerAttackNotificationType.PlayerLeft,
                    JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = lobby.MapToDTO() }, JsonOptions.Get())),
                cancellationToken
            );
        }
    }

    public async Task<IEnumerable<PlayerDTO>> ShutDownLobbyAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var lobby = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        if (lobby is null)
            return Enumerable.Empty<PlayerDTO>();

        await lobbyRepository.DeleteAsync(lobbyId, cancellationToken);

        var players = lobby.Players.ToList();

        foreach (var player in players) await notificationHelper.LeaveLobbyGroupForUserAsync(player.Id, lobbyId, cancellationToken);

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

        return players.Select(x => x.MapToDTO());
    }

    public async Task<IEnumerable<PlayerDTO>> GetPlayersAsync(string lobbyId, CancellationToken cancellationToken = default)
    {   
        var lobby = await lobbyRepository.GetAsync(lobbyId, cancellationToken);
        return lobby?.Players.Select(x => x.MapToDTO()) ?? Enumerable.Empty<PlayerDTO>();
    }

    public async Task UpdatePlayerAsync(PlayerDTO player, CancellationToken cancellationToken = default)
    {
        // 1. Find the lobby containing the player
        var lobbiesKvps = await lobbyRepository.GetAllAsync(cancellationToken);
        KeyValuePair<string, Lobby>? lobbyKvp = null;
        foreach (var kvp in lobbiesKvps)
        {
            if (kvp.Value.Players.Any(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase)))
            {
                lobbyKvp = kvp;
                break;
            }
        }
        if (!lobbyKvp.HasValue)
            return;

        var lobby = lobbyKvp.Value.Value;
        var lobbyId = lobbyKvp.Value.Key;

        // 2. Update the player in the Players collection
        lock (lobby.Players)
        {
            var existingPlayer = lobby.Players.FirstOrDefault(x => x.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase));
            if (existingPlayer == null)
                throw new KeyNotFoundException("Player not found in the lobby.");

            lobby.Players.Remove(existingPlayer);
            lobby.Players.Add(player.MapToModel());
        }

        // 3. If the player is the host, update HostPlayer as well
        if (lobby.HostPlayer.Id.Equals(player.Id, StringComparison.InvariantCultureIgnoreCase))
        {
            lobby.HostPlayer = player.MapToModel();
        }

        // 4. Update the lobby in the repository
        await lobbyRepository.UpdateAsync(lobbyId, lobby, cancellationToken);

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
    }

    public async Task StartGameAsync(string lobbyId, CancellationToken cancellationToken = default)
    {
        var lobbyDTO = await GetLobbyAsync(lobbyId, cancellationToken);
        if (lobbyDTO is null) return;

        string activeGameId = Guid.NewGuid().ToString();
        lobbyDTO.GameId = activeGameId;
        await lobbyRepository.UpdateAsync(lobbyId, lobbyDTO.MapToModel(), cancellationToken);

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
        };
        await activeGameRepository.AddAsync(activeGameId, activeGame, cancellationToken);
        await gameStateRepository.AddAsync(activeGameId, GameStates.Freebie, cancellationToken);

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

        var lobby = await activeGameRepository.GetAsync(activeGameId, cancellationToken)
            ?? throw new KeyNotFoundException("Game not found");

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
            await gamePlayerRepository.AddAsync(player.Id, gamePlayer, cancellationToken);
            await notificationHelper.JoinGameGroupForUserAsync(player.Id, activeGameId, cancellationToken);
        }
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

    async Task RemovePlayerFromAllLobbiesAsync(string playerId, CancellationToken cancellationToken)
    {
        var allLobbyKVPs = await lobbyRepository.GetAllAsync(cancellationToken);

        foreach (var kvp in allLobbyKVPs)
        {
            var lobby = kvp.Value;
            if (!lobby.Players.Select(x => x.Id).Contains(playerId))
                continue;

            if (lobby.HostPlayer.Id == playerId)
            {
                // Shut down the old lobby if they were host
                var players = lobby.Players.ToList();
                await lobbyRepository.DeleteAsync(kvp.Key, cancellationToken);

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
                await lobbyRepository.UpdateAsync(kvp.Key, lobby, cancellationToken);

                await notificationHelper.BroadcastToAllAsync(
                    new PokerAttackNotification(
                        PokerAttackNotificationType.PlayerLeft,
                        JsonSerializer.Serialize(new LobbyEventArgs() { Lobby = lobby.MapToDTO() }, JsonOptions.Get()))
                );
            }
        }
    }
}
