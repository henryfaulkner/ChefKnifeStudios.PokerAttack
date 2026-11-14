using ChefKnifeStudios.PokerAttack.Server.Core.Interfaces;
using ChefKnifeStudios.PokerAttack.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace ChefKnifeStudios.PokerAttack.Server.Infrastructure;

public class ExpiredStateSweeper(AppDbContext dbContext) : IExpiredStateSweeper
{
    public async Task<IReadOnlyList<(string Type, string Key)>> SweepExpiredAsync(DateTime now, CancellationToken ct = default)
    {
        var expired = await dbContext.StateKeyValues
            .Where(x => x.ExpiresAt != null && x.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return [];

        var removed = expired.Select(x => (x.Type, x.Key)).ToList();

        dbContext.StateKeyValues.RemoveRange(expired);
        await dbContext.SaveChangesAsync(ct);

        return removed;
    }
}
