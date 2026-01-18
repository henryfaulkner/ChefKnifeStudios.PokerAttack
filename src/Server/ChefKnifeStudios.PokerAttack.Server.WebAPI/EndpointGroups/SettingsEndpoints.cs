using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;
using Microsoft.Extensions.Options;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.Settings;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Settings")
            .WithTags("Settings");

        group.MapGet(Endpoints.GetGameSettings, (
            IOptions<GameSettings> gameSettings) =>
        {
            var settings = gameSettings.Value;
            var dto = new GameSettingsDTO(
                settings.RoundTimeMs,
                settings.ShopTimeMs,
                settings.EliminationTimeMs,
                settings.PlayerPowerSelectionTimeMs
            );
            return Result.Success(dto);
        })
        .WithName(nameof(Endpoints.GetGameSettings))
        .Produces<GameSettingsDTO>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
