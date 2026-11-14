using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Core.Models;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class PlayerSessionStore(AppDbContext dbContext) : IPlayerSessionStore
{
    public async Task<Result> UpsertAsync(PlayerSession session, CancellationToken ct = default)
    {
        var row = await dbContext.PlayerSessions.FindAsync([session.PlayerId], ct);
        if (row is null)
        {
            dbContext.PlayerSessions.Add(new PlayerSessionEntity
            {
                PlayerId = session.PlayerId,
                GameId = session.GameId,
                LobbyId = session.LobbyId,
                Connected = session.Connected,
                DisconnectedAt = session.DisconnectedAt,
                UpdatedAt = session.UpdatedAt,
            });
        }
        else
        {
            row.GameId = session.GameId;
            row.LobbyId = session.LobbyId;
            row.Connected = session.Connected;
            row.DisconnectedAt = session.DisconnectedAt;
            row.UpdatedAt = session.UpdatedAt;
        }

        await dbContext.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<PlayerSession?>> GetByPlayerAsync(string playerId, CancellationToken ct = default)
    {
        var row = await dbContext.PlayerSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlayerId == playerId, ct);

        return Result.Success(row is null ? null : Map(row));
    }

    public async Task<Result<IReadOnlyList<PlayerSession>>> FindByGameAsync(string gameId, CancellationToken ct = default)
    {
        var rows = await dbContext.PlayerSessions.AsNoTracking()
            .Where(x => x.GameId == gameId)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<PlayerSession>>(rows.Select(Map).ToList());
    }

    public async Task<Result<IReadOnlyList<PlayerSession>>> FindByLobbyAsync(string lobbyId, CancellationToken ct = default)
    {
        var rows = await dbContext.PlayerSessions.AsNoTracking()
            .Where(x => x.LobbyId == lobbyId)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<PlayerSession>>(rows.Select(Map).ToList());
    }

    public async Task<Result<IReadOnlyList<PlayerSession>>> FindOverdueByDisconnectedAtAsync(DateTime threshold, CancellationToken ct = default)
    {
        var rows = await dbContext.PlayerSessions.AsNoTracking()
            .Where(x => x.DisconnectedAt != null && x.DisconnectedAt <= threshold)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<PlayerSession>>(rows.Select(Map).ToList());
    }

    static PlayerSession Map(PlayerSessionEntity row) => new()
    {
        PlayerId = row.PlayerId,
        GameId = row.GameId,
        LobbyId = row.LobbyId,
        Connected = row.Connected,
        DisconnectedAt = row.DisconnectedAt,
        UpdatedAt = row.UpdatedAt,
    };
}
