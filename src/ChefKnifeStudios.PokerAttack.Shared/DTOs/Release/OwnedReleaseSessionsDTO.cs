using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.Release;

public sealed record OwnedReleaseLobbyDTO(string Id, PlayerDTO HostPlayer);
public sealed record OwnedReleaseSoloDTO(string GameId, string PlayerId);
public sealed record OwnedReleaseSessionsDTO(int InFlightOperations,
    IReadOnlyList<OwnedReleaseLobbyDTO> Lobbies, IReadOnlyList<string> Games,
    IReadOnlyList<OwnedReleaseSoloDTO> Solo);
