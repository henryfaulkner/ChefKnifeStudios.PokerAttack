using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ChefKnifeStudios.PokerAttack.Server.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // No implicit local/production secret file discovery. A null connection allows
        // scaffolding/bundling; executing requires this explicit environment value or --connection.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__PokerAttackDB")
            ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__POKERATTACKDB");

        optionsBuilder.UseNpgsql(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
