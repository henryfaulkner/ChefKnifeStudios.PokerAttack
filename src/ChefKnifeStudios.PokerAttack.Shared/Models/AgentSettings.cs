namespace ChefKnifeStudios.PokerAttack.Shared.Models;

/// <summary>
/// Configuration settings for AI agent behavior
/// </summary>
public class AgentSettings
{
    /// <summary>
    /// Whether AI agents are enabled globally
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Minimum think time in milliseconds between actions
    /// </summary>
    public int MinThinkTimeMs { get; set; } = 500;

    /// <summary>
    /// Maximum think time in milliseconds between actions
    /// </summary>
    public int MaxThinkTimeMs { get; set; } = 1500;

    /// <summary>
    /// Type of brain to use for decision making (e.g., "Random", "Aggressive", "Conservative")
    /// </summary>
    public string BrainType { get; set; } = "Random";

    /// <summary>
    /// Whether agents should automatically join available lobbies
    /// </summary>
    public bool AutoJoinLobby { get; set; } = true;

    /// <summary>
    /// Whether agents should create a new lobby if none available
    /// </summary>
    public bool AutoCreateLobby { get; set; } = false;

    /// <summary>
    /// Maximum number of players before host starts the game
    /// </summary>
    public int MaxPlayersBeforeStart { get; set; } = 4;

    /// <summary>
    /// Whether agent should auto-start game if they are the host
    /// </summary>
    public bool AutoStartGameAsHost { get; set; } = true;

    /// <summary>
    /// Default game mode for agent-started games (e.g., "Quick", "Standard")
    /// </summary>
    public string DefaultGameMode { get; set; } = "Quick";
}
