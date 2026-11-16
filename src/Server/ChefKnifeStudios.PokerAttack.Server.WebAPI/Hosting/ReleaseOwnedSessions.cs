using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Lobby;
using ChefKnifeStudios.PokerAttack.Shared.DTOs.Release;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;

public sealed partial class ReleaseAdmission
{
    // Only authorized, randomly allocated session IDs live here. No credential,
    // hand/card data, disk writes, global repository enumeration or deletion.
    sealed class OwnedAttempt(Grant grant)
    {
        public Grant Grant { get; } = grant;
        public Dictionary<string, (string Kind, string? Player)> Sessions { get; } = [];
    }
    readonly object _ownedLock = new();
    readonly Dictionary<string, OwnedAttempt> _owned = [];
    int _creating;

    public IDisposable ObserveCreation()
    {
        Interlocked.Increment(ref _creating);
        return new CreationScope(this);
    }

    sealed class CreationScope(ReleaseAdmission admission) : IDisposable
    {
        bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Interlocked.Decrement(ref admission._creating);
        }
    }

    bool RememberGrant(HttpContext context, Grant grant)
    {
        lock (_ownedLock)
        {
            if (!_owned.TryGetValue(grant.Id, out var attempt))
            {
                if (_owned.Count >= 64 || _verifierPlayers.Count > 4096) return false;
                _owned.Add(grant.Id, attempt = new OwnedAttempt(grant));
            }
            if (attempt.Grant.Owner != grant.Owner) return false;
            context.Items[typeof(OwnedAttempt)] = attempt;
            return true;
        }
    }

    public void RememberSession(HttpContext context, string kind, string id, string? playerId = null)
    {
        if (!context.Items.TryGetValue(typeof(OwnedAttempt), out var value)) return;
        var attempt = (OwnedAttempt)value!;
        lock (_ownedLock)
        {
            if (kind is not ("lobby" or "game" or "solo") || id.Length > 64 || attempt.Sessions.Count >= 3)
                throw new InvalidOperationException("Release session allocation exceeded its bound.");
            attempt.Sessions.Add(id, (kind, playerId));
            if (playerId is not null) _verifierPlayers.TryAdd(playerId, attempt.Grant.Id);
        }
    }

    public async Task<OwnedReleaseSessionsDTO?> OwnedSessionsAsync(HttpContext context, CancellationToken token)
    {
        var grant = Credential(context);
        if (grant is null) return null;
        (OwnedAttempt Attempt, KeyValuePair<string, (string Kind, string? Player)>[] Sessions)[] attempts;
        lock (_ownedLock)
        {
            // Includes requests paused before credential validation. Once an old
            // token is revoked, wait for its admitted creation to finish before
            // listing exact IDs, including a lost response or canceled runner.
            var creating = Volatile.Read(ref _creating);
            if (creating != 0) return new(creating, [], [], []);
            attempts = _owned.Values.Where(attempt => attempt.Grant.Owner == grant.Owner)
                .Select(attempt => (attempt, attempt.Sessions.ToArray())).ToArray();
        }
        var lobbies = new List<OwnedReleaseLobbyDTO>();
        var games = new List<string>();
        var solo = new List<OwnedReleaseSoloDTO>();
        foreach (var (attempt, sessions) in attempts)
        {
            foreach (var (id, (kind, player)) in sessions)
            {
                bool exists;
                if (kind == "lobby")
                {
                    var result = await context.RequestServices.GetRequiredService<IKeyValueRepository<Lobby>>().GetAsync(id, token);
                    if (!result.IsSuccess) throw new InvalidOperationException("Release lobby lookup unavailable.");
                    exists = result.Value is not null;
                    if (exists)
                    {
                        var lobby = result.Value!;
                        if (!lobby.Players.All(p => attempt.Grant.Players.ContainsKey(p.Id)) || !attempt.Grant.Players.TryGetValue(lobby.HostPlayer.Id, out var name))
                            throw new InvalidOperationException("Release lobby ownership changed.");
                        lobbies.Add(new(id, new PlayerDTO { Id = lobby.HostPlayer.Id, Name = name }));
                    }
                }
                else if (kind == "game")
                {
                    var result = await context.RequestServices.GetRequiredService<IKeyValueRepository<ActiveGame>>().GetAsync(id, token);
                    if (!result.IsSuccess) throw new InvalidOperationException("Release game lookup unavailable.");
                    exists = result.Value is not null;
                    if (exists)
                    {
                        if (!result.Value!.Players.All(p => attempt.Grant.Players.ContainsKey(p.Id)))
                            throw new InvalidOperationException("Release game ownership changed.");
                        games.Add(id);
                    }
                }
                else
                {
                    var result = await context.RequestServices.GetRequiredService<IKeyValueRepository<SoloGame>>().GetAsync(id, token);
                    if (!result.IsSuccess) throw new InvalidOperationException("Release solo lookup unavailable.");
                    exists = result.Value is not null;
                    if (exists)
                    {
                        if (result.Value!.PlayerId != player) throw new InvalidOperationException("Release solo ownership changed.");
                        solo.Add(new(id, player!));
                    }
                }
                if (!exists) lock (_ownedLock) attempt.Sessions.Remove(id);
            }
            lock (_ownedLock)
                if (attempt.Sessions.Count == 0) _owned.Remove(attempt.Grant.Id);
        }
        return new(0, lobbies, games, solo);
    }
}
