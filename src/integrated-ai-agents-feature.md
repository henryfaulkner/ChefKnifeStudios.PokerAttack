# Feature Design Document: Integrated AI Agents (Auto-Pilot)

## 1. Overview

This feature introduces "Self-Driving" agents into the Blazor WASM client. These agents utilize the same game logic, SignalR handlers, and state management as real users, allowing for high-fidelity automated testing and providing a foundation for future single-player AI opponents.

## 2. Goals

- **Reproduce Sync Issues**: Run full game lifecycles in a real browser environment to catch WASM-specific crashes and SignalR race conditions.
- **Zero-Maintenance UI Testing**: Bypass brittle Selenium "element clicking" by triggering game actions directly through C# code.
- **Future Utility**: Reuse the agent logic as AI opponents for single-player modes.

## 3. Architecture

The design follows a "Headless Client" pattern. The agent lives inside the Client project but operates without human input.

### 3.1 Components

- **IAgentBrain (Shared Library)**: Defines the decision-making interface.
- **AutoPilotService (Client Project)**: A scoped service that monitors the GameState and invokes actions.
- **AgentOrchestrator (Test Tool)**: A lightweight Playwright/Node.js script that launches multiple browser instances with the `?autoPlay=true` flag.

## 4. Technical Specification

### 4.1 The Brain Interface (Shared Project)

Defining this in the Shared project ensures the Server can also use it later for server-side bots.

```csharp
public interface IAgentBrain
{
    string Name { get; }

    // Analyzes the state and returns an action to take
    GameAction DecideAction(GameState currentState, Guid localPlayerId);
}
```

### 4.2 The Auto-Pilot Service (Client Project)

This service acts as the "Glue" between the Brain and your existing SignalR Hub connection.

```csharp
public class AutoPilotService
{
    private readonly IAgentBrain _brain;
    private readonly GameStateContainer _state; // Your existing state store
    private readonly HubConnection _hub;

    public bool IsEnabled { get; private set; }

    public void Enable() => IsEnabled = true;

    // Call this whenever the SignalR Hub updates the GameState
    public async Task ProcessStateUpdate()
    {
        if (!IsEnabled || _state.CurrentTurnPlayerId != _state.LocalPlayerId)
            return;

        // Simulate human thought time
        await Task.Delay(Random.Shared.Next(500, 1500));

        var action = _brain.DecideAction(_state.CurrentGame, _state.LocalPlayerId);
        await _hub.SendAsync("SendAction", action);
    }
}
```

### 4.3 Triggering via URL

In `App.razor` or your main `Game.razor` component:

```csharp
protected override void OnInitialized()
{
    var uri = NavigationManager.ToAbsoluteUri(NavigationManager.Uri);
    if (QueryHelpers.ParseQuery(uri.Query).ContainsKey("autoPlay"))
    {
        AutoPilot.Enable();
    }
}
```

## 5. Automated Testing Workflow (The "Nightly Run")

Instead of complex Selenium scripts, your "Agent" script is now trivial:

1. **Launch**: Use Playwright to open $N$ browser instances.
2. **Navigate**: Go to `https://game.com/lobby?autoPlay=true&room=TestRoom1`.
3. **Observe**:
   - Monitor the console for Sentry logs.
   - Set a "Timeout" watch. If the URL doesn't reach the `/results` page within 5 minutes, the test fails.
4. **Report**: Save screenshots only on failure.

## 6. Implementation Phases

| Phase | Task | Description |
|-------|------|-------------|
| Phase 1 | Interface Setup | Move GameState and Actions to a Shared library if not already there. |
| Phase 2 | The "Dumb" Bot | Implement a RandomActionBrain that simply picks any valid move. |
| Phase 3 | State Hook | Inject AutoPilotService into your Game component and hook into SignalR OnReceiveUpdate. |
| Phase 4 | Orchestration | Write the Node.js/Playwright script to spin up 4-8 players in a container. |

## 7. Monitoring & Debugging

Since these agents run in a real browser:

- **Sentry**: All WASM exceptions will be tagged with `user: agent`.
- **Screenshots**: Playwright can record a video of the "Agent" playing, allowing you to see exactly where the UI froze.
- **Logs**: The Agent can "Speak" to the console (e.g., `console.log("Waiting for Round 2...")`), which is captured by the test runner.
