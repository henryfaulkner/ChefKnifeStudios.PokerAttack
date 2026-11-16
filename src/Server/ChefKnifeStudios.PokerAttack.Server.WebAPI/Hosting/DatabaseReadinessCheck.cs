using System.Data;
using ChefKnifeStudios.PokerAttack.Server.Data;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ChefKnifeStudios.PokerAttack.Server.WebAPI.Hosting;

public sealed class DatabaseReadinessCheck(IServiceScopeFactory scopes, IHostApplicationLifetime lifetime) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!lifetime.ApplicationStarted.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested)
            return HealthCheckResult.Unhealthy("Application is not ready.");

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var scope = scopes.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            database.Database.SetCommandTimeout(3);
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, bounded.Token);
            await database.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", bounded.Token);
            if ((await database.Database.GetPendingMigrationsAsync(bounded.Token)).Any())
                return HealthCheckResult.Unhealthy("Database is unavailable or incompatible.");

            // Empty projections validate mapped tables/columns without reading player data or changing schema.
            await database.Set<Game>().AsNoTracking().Take(0).ToListAsync(bounded.Token);
            await database.Set<Round>().AsNoTracking().Take(0).ToListAsync(bounded.Token);
            await database.Set<RoundScore>().AsNoTracking().Take(0).ToListAsync(bounded.Token);
            await transaction.RollbackAsync(bounded.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Public probe responses and health reports never include provider exceptions/credentials.
            return HealthCheckResult.Unhealthy("Database is unavailable or incompatible.");
        }
    }
}
