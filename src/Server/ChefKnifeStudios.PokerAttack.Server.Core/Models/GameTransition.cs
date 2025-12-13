using ChefKnifeStudios.PokerAttack.Shared.Enums;

namespace ChefKnifeStudios.PokerAttack.Server.Core.Models;

public record GameTransition(GameStates CurrState, GameEvents GameEvent, GameStates NextState);

public static class GameTransitions
{
    public static GameTransition? Get(GameStates currState, GameEvents gameEvent, GameModes gameMode) =>
        Get(gameMode).FirstOrDefault(x => x.CurrState == currState && x.GameEvent == gameEvent);

    static GameTransition[] Get(GameModes gameMode) =>
        gameMode switch
        {
            GameModes.Quick => 
                new GameTransition[]
                {
                    new GameTransition(GameStates.GameStart, GameEvents.Next, GameStates.Freebie),
                    new GameTransition(GameStates.Freebie, GameEvents.Next, GameStates.InGame),
                    new GameTransition(GameStates.InGame, GameEvents.Next, GameStates.Scoreboard),
                    new GameTransition(GameStates.Scoreboard, GameEvents.Next, GameStates.Elimination),
                    new GameTransition(GameStates.Scoreboard, GameEvents.Eliminate, GameStates.Elimination),
                    new GameTransition(GameStates.Elimination, GameEvents.Next, GameStates.InGame),
                },
            GameModes.Full =>
                new GameTransition[]
                {
                    new GameTransition(GameStates.GameStart, GameEvents.Next, GameStates.Freebie),
                    new GameTransition(GameStates.Freebie, GameEvents.Next, GameStates.InGame),
                    new GameTransition(GameStates.InGame, GameEvents.Next, GameStates.Scoreboard),
                    new GameTransition(GameStates.Scoreboard, GameEvents.Next, GameStates.Shop),
                    new GameTransition(GameStates.Scoreboard, GameEvents.Eliminate, GameStates.Elimination),
                    new GameTransition(GameStates.Shop, GameEvents.Next, GameStates.InGame),
                    new GameTransition(GameStates.Elimination, GameEvents.Next, GameStates.Shop),
                },
        };
}
