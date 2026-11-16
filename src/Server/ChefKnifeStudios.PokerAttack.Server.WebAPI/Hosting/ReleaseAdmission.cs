using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using Microsoft.AspNetCore.SignalR;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;

public sealed class RequiresNewSessionAdmission;

/// <summary>Reads host-owned state on each admission; the API never writes it.</summary>
public sealed partial class ReleaseAdmission(IConfiguration configuration, TimeProvider? clock = null)
{
    public const string VerifierCookie = "__Host-PokerAttackVerifier";
    readonly ConcurrentDictionary<string, HashSet<string>> _admissions = new();
    readonly ConcurrentDictionary<string, string> _verifierPlayers = new();

    JsonDocument? ReadState()
    {
        var path = configuration["Hosting:MaintenanceStatePath"];
        if (string.IsNullOrEmpty(path)) return null;
        using var stream = File.OpenRead(path);
        if (stream.Length > 65536) throw new InvalidDataException();
        return JsonDocument.Parse(stream);
    }

    public bool Active
    {
        get
        {
            var path = configuration["Hosting:MaintenanceStatePath"];
            // Existing local/development fixtures do not mount production state.
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                using var document = ReadState();
                return document!.RootElement.GetProperty("active").GetBoolean();
            }
            catch
            {
                // A missing/malformed configured mount never reopens admissions.
                return true;
            }
        }
    }

    sealed record Owner(long Run, string Digest, string Config, string Source);
    sealed record Grant(string Id, Dictionary<string, string> Players, Owner Owner);

    bool IsVerifierPlayer(string? playerId)
    {
        if (playerId is not null && _verifierPlayers.ContainsKey(playerId)) return true;
        try
        {
            using var state = ReadState();
            return state?.RootElement.GetProperty("verifier").GetProperty("players").EnumerateArray()
                .Any(player => player.GetProperty("id").GetString() == playerId) == true;
        }
        catch { return false; }
    }

    Grant? Credential(HttpContext? context)
    {
        try
        {
            // Browser WebSockets cannot set Authorization. The protected verifier
            // job installs a host-only Secure/HttpOnly cookie in its isolated
            // browser context; HTTP/Node clients use standard Bearer transport.
            if (context is null || !context.Request.IsHttps) return null;
            var authorization = context.Request.Headers.Authorization;
            if (authorization.Count > 1) return null;
            var header = authorization.ToString();
            if (header.Length > 0 && !header.StartsWith("Bearer ", StringComparison.Ordinal)) return null;
            var bearer = header.Length > 0 ? header[7..] : null;
            var cookie = context.Request.Cookies[VerifierCookie];
            if (bearer is not null && cookie is not null && bearer != cookie) return null;
            var supplied = bearer ?? cookie;
            if (supplied is null || supplied.Length > 128) return null;
            var parts = supplied.Split('.');
            if (parts.Length != 3 || parts[2].Length != 64 || parts[2].Any(c => !char.IsAsciiHexDigitLower(c))) return null;
            using var document = ReadState();
            var state = document!.RootElement;
            if (!state.GetProperty("active").GetBoolean() || parts[0] != state.GetProperty("run_id").GetRawText() ||
                parts[1] != state.GetProperty("run_attempt").GetRawText()) return null;
            if (state.GetProperty("digest").GetString() != configuration["Hosting:ReleaseImage"]?.Split('@').Last() ||
                state.GetProperty("config_sha256").GetString() != configuration["Hosting:ReleaseConfigSha256"] ||
                state.GetProperty("source_sha").GetString() != configuration["POKERATTACK_SOURCE_REVISION"]) return null;
            var verifier = state.GetProperty("verifier");
            var now = (clock ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds();
            var issued = verifier.GetProperty("issued_unix").GetInt64();
            var expires = verifier.GetProperty("expires_unix").GetInt64();
            if (verifier.GetProperty("revoked").GetBoolean() || issued > now || expires <= now ||
                expires <= issued || expires - issued > 1800) return null;
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(parts[2]));
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(verifier.GetProperty("token_sha256").GetString()!))) return null;
            var players = verifier.GetProperty("players").EnumerateArray().ToDictionary(
                player => player.GetProperty("id").GetString()!, player => player.GetProperty("name").GetString()!);
            if (players.Count is < 2 or > 3) return null;
            var grantId = verifier.GetProperty("id").GetString()!;
            foreach (var player in players.Keys)
                if (_verifierPlayers.GetOrAdd(player, grantId) != grantId) return null;
            return new Grant(grantId, players,
                new Owner(state.GetProperty("run_id").GetInt64(), state.GetProperty("digest").GetString()!,
                    state.GetProperty("config_sha256").GetString()!, state.GetProperty("source_sha").GetString()!));
        }
        catch { return null; }
    }

    static JsonElement Property(JsonElement element, string name)
    {
        var matches = element.EnumerateObject().Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException();
        return matches[0].Value;
    }

    public async Task<bool> AllowNewSessionAsync(HttpContext context)
    {
        if (!Active) return true;
        var grant = Credential(context);
        if (grant is null) return false;
        try
        {
            context.Request.EnableBuffering(bufferThreshold: 4096, bufferLimit: 16384);
            using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            var body = document.RootElement;
            var path = context.Request.Path.Value?.TrimEnd('/').ToLowerInvariant();
            string reservation;
            bool Player(JsonElement element)
            {
                var id = Property(element, "id").GetString()!;
                return grant.Players.TryGetValue(id, out var name) && name == Property(element, "name").GetString();
            }
            async Task<bool> OwnedLobby()
            {
                var lobbyId = Property(body, "lobbyId").GetString()!;
                var lobby = await context.RequestServices.GetRequiredService<IKeyValueRepository<Lobby>>().GetAsync(lobbyId, context.RequestAborted);
                return lobby.IsSuccess && lobby.Value is not null && grant.Players.ContainsKey(lobby.Value.HostPlayer.Id) &&
                    lobby.Value.Players.All(player => grant.Players.ContainsKey(player.Id));
            }
            switch (path)
            {
                case "/lobby/create-lobby":
                    if (!Player(Property(body, "hostPlayer"))) return false;
                    reservation = "create-lobby";
                    break;
                case "/lobby/add-player":
                    if (!Player(Property(body, "player")) || !await OwnedLobby()) return false;
                    reservation = "join-" + Property(Property(body, "player"), "id").GetString();
                    break;
                case "/lobby/start-game":
                    if (!await OwnedLobby()) return false;
                    reservation = "start-game";
                    break;
                case "/solo/start-game":
                    if (!grant.Players.ContainsValue(Property(body, "playerName").GetString()!)) return false;
                    reservation = "start-solo";
                    break;
                default: return false;
            }
            var reservations = _admissions.GetOrAdd(grant.Id, _ => []);
            lock (reservations)
            {
                if (!reservations.Add(reservation)) return false;
                return RememberGrant(context, grant);
            }
        }
        catch { return false; }
        finally { if (context.Request.Body.CanSeek) context.Request.Body.Position = 0; }
    }

    public async Task<bool> CanConnectAsync(string? playerId, IServiceProvider services, CancellationToken token, HttpContext? context = null)
    {
        if (!Active) return true;
        if (string.IsNullOrEmpty(playerId)) return false;
        if (IsVerifierPlayer(playerId)) return Credential(context)?.Players.ContainsKey(playerId) == true;
        var games = await services.GetRequiredService<IKeyValueRepository<ActiveGame>>().GetAllAsync(token);
        var solo = await services.GetRequiredService<IKeyValueRepository<SoloGame>>().GetAllAsync(token);
        return games.IsSuccess && solo.IsSuccess &&
            (games.Value.Values.Any(game => game.Players.Any(player => player.Id == playerId)) ||
             solo.Value.Values.Any(game => game.PlayerId == playerId));
    }

    public bool CanInvoke(string? playerId, HttpContext? context) => !Active || !IsVerifierPlayer(playerId) ||
        Credential(context)?.Players.ContainsKey(playerId!) == true;
}

public sealed class ReleaseAdmissionHubFilter(ReleaseAdmission admission) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        if (!await admission.CanConnectAsync(context.Context.UserIdentifier, context.ServiceProvider, context.Context.ConnectionAborted, context.Context.GetHttpContext()))
            throw new HubException("PokerAttack is in maintenance. Reload after maintenance to start a fresh session.");
        await next(context);
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!admission.CanInvoke(context.Context.UserIdentifier, context.Context.GetHttpContext()))
            throw new HubException("Release verification admission expired or was revoked.");
        return await next(context);
    }
}
