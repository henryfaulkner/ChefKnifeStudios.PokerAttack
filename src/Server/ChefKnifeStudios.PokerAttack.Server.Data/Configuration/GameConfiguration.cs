using ChefKnifeStudios.PokerAttack.Server.Data.Constants;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Configuration;

internal class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("Games", DbSchemas.PokerAttack);

        builder.HasKey(x => x.Id);
        builder.Property(e => e.Id)
          .ValueGeneratedOnAdd()
          .UseIdentityColumn();
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasMany(x => x.Rounds)
            .WithOne(x => x.Game);
    }
}
