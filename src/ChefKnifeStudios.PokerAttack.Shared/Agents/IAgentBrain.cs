using ChefKnifeStudios.PokerAttack.Shared.DTOs.Agent;

namespace ChefKnifeStudios.PokerAttack.Shared.Agents;

/// <summary>
/// Interface for AI agent decision-making logic
/// </summary>
public interface IAgentBrain
{
    /// <summary>
    /// Name of the agent brain for identification
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Decides what action to take based on current game state
    /// </summary>
    /// <param name="gameState">Immutable snapshot of current game state</param>
    /// <param name="localPlayerId">The agent's player ID</param>
    /// <returns>The action to take, or null if no action should be taken</returns>
    AgentAction? DecideAction(AgentGameState gameState, Guid localPlayerId);
}
