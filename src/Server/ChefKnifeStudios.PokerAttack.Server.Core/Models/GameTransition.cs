using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public record GameTransition(GameStates CurrState, GameEvents GameEvent, GameStates NextState);

public static class GameTransitions
{
    public static GameTransition? Get(GameStates currState, GameEvents gameEvent) =>
        Get().FirstOrDefault(x => x.CurrState == currState && x.GameEvent == gameEvent);

    public static GameTransition[] Get() =>
        new GameTransition[]
        {
            new GameTransition(GameStates.GameStart, GameEvents.Next, GameStates.Upgrade),
            new GameTransition(GameStates.Upgrade, GameEvents.Next, GameStates.InGame),
            new GameTransition(GameStates.InGame, GameEvents.Next, GameStates.Scoreboard),
            new GameTransition(GameStates.Scoreboard, GameEvents.Next, GameStates.Elimination),
            new GameTransition(GameStates.Elimination, GameEvents.Next, GameStates.InGame),
        };
}
