namespace ChefKnifeStudios.PokerAttack.Shared.DTOs.SignalR.EventArgs;

public sealed record EliminatingPlayerDTO(string PlayerId, string PlayerName);

public sealed record EliminationStartedArgs(List<EliminatingPlayerDTO> Players);

public sealed record EliminationFinishedArgs(List<EliminatingPlayerDTO> Losers, EliminatingPlayerDTO? Winner);