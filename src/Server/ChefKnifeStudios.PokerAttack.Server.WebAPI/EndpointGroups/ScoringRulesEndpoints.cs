using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.BL.Services;
using ChefKnifeStudios.PokerAttack.Shared.DTOs;

using Endpoints = ChefKnifeStudios.PokerAttack.Shared.PokerAttackApiEndpoints.ScoringRules;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.EndpointGroups;

public static class ScoringRulesEndpoints
{
    public static IEndpointRouteBuilder MapScoringRulesEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder
            .MapGroup(string.Empty)
            .WithName("Scoring Rules")
            .WithTags("Scoring Rules");

        group.MapGet(Endpoints.GetScoringRules, async (
            IScoringRulesService scoringRulesService,
            CancellationToken cancellationToken = default) =>
        {
            var scoringRules = scoringRulesService.GetScoringRules(cancellationToken);
            return Result.Success(scoringRules);
        })
        .WithName(nameof(Endpoints.GetScoringRules))
        .Produces<ScoringRules>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        return builder;
    }
}
