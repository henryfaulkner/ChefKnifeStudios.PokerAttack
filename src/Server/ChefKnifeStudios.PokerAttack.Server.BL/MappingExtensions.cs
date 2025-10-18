using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Gameplay;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;

namespace ChefKnifeStudios.PokerAttack.Server.BL;

public static class MappingExtensions
{
    public static LobbyDTO MapToDTO(this Lobby model, string gameId)
    {
        return new()
        {
            GameId = gameId,
            HostPlayer = model.HostPlayer.MapToDTO(),
            InProgress = model.InProgress,
            Players = model.Players.Select(x => x.MapToDTO()).ToHashSet(),
        };
    }

    public static Lobby MapToModel(this LobbyDTO dto)
    {
        return new()
        {
            HostPlayer = dto.HostPlayer.MapToModel(),
            InProgress = dto.InProgress,
            Players = dto.Players.Select(x => x.MapToModel()).ToHashSet(),
        };
    }

    public static PlayerDTO MapToDTO(this Player model)
    {
        return new()
        {
            Id = model.Id,
            Name = model.Name,
        };
    }

    public static Player MapToModel(this PlayerDTO dto)
    {
        return new()
        {
            Id = dto.Id,
            Name = dto.Name,
        };
    }

    public static CardDTO MapToDTO(this Card model)
    {
        return new()
        {
            Suit = model.Suit,
            Rank = model.Rank,
            IsFaceDown = model.IsFaceDown,
        };
    }

    public static Card MapToModel(this CardDTO dto)
    {
        return new()
        {
            Suit = dto.Suit,
            Rank = dto.Rank,
            IsFaceDown = dto.IsFaceDown,
        };
    }

    public static HandResultDTO MapToDTO(this HandResult model, int totalPlayerScore)
    {
        return new()
        {
            HandType = model.HandType,
            CardValues = model.CardValues,
            BaseChips = model.BaseChips,
            BaseMultiplier = model.BaseMultiplier,
            HandScore = model.HandScore,
            TotalPlayerScore = totalPlayerScore,
        };
    }

    public static HandResult MapToModel(this HandResultDTO dto)
    {
        return new()
        {
            HandType = dto.HandType,
            CardValues = dto.CardValues,
            BaseChips = dto.BaseChips,
            BaseMultiplier = dto.BaseMultiplier,
            HandScore = dto.HandScore,
        };
    }

    public static RoundDTO MapToDTO(this Round model)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var dto = new RoundDTO
        {
            Scores = model.RoundScores?
                .Select(rs => new RoundDTO.RoundScoreDTO
                {
                    ClientUserId = rs.ClientUserId,
                    ClientUserDisplayName = rs.ClientUserDisplayName,
                    Score = rs.Score
                })
                .ToList() ?? new List<RoundDTO.RoundScoreDTO>()
        };

        return dto;
    }

    public static PlayerPowerDTO MapToDTO(this PlayerPower model)
    {
        return new PlayerPowerDTO
        {
            Id = model.Id,
            Name = model.Name,
            Description = model.Description,
            PointCost = model.PointCost,
        };
    }
}
