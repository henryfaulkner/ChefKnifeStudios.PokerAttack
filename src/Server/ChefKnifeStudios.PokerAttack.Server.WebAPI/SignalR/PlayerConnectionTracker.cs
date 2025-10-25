using System.Collections.Concurrent;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.SignalR;

public interface IPlayerConnectionTracker
{
    void Add(string userId, string connectionId);
    void Remove(string userId, string connectionId);
    IReadOnlyCollection<string> GetConnections(string userId);
}

public class PlayerConnectionTracker : IPlayerConnectionTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _map = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string userId, string connectionId)
    {
        var set = _map.GetOrAdd(userId, _ => new HashSet<string>());
        lock (set)
        {
            set.Add(connectionId);
        }
    }

    public void Remove(string userId, string connectionId)
    {
        if (_map.TryGetValue(userId, out var set))
        {
            lock (set)
            {
                set.Remove(connectionId);
                if (set.Count == 0)
                    _map.TryRemove(userId, out _);
            }
        }
    }

    public IReadOnlyCollection<string> GetConnections(string userId)
    {
        if (_map.TryGetValue(userId, out var set))
        {
            lock (set)
            {
                return set.ToList().AsReadOnly();
            }
        }

        return Array.Empty<string>();
    }
}
