using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Client.Shared.Services;

public interface IInputService
{
    void RegisterKeyAction(string key, Func<Task> action);
    void UnregisterKeyAction(string key);
    Task HandleKeyAsync(string key);
}

public class InputService : IInputService
{
    readonly ConcurrentDictionary<string, Func<Task>> _keyActions = new();

    public void RegisterKeyAction(string key, Func<Task> action)
        => _keyActions[key] = action;

    public void UnregisterKeyAction(string key)
        => _keyActions.TryRemove(key, out _);

    public Task HandleKeyAsync(string key)
        => _keyActions.TryGetValue(key, out var action) ? action() : Task.CompletedTask;
}
