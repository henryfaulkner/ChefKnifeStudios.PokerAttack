using ChefKnifeStudios.PokerAttack.Server.Data.Constants;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Configuration;

internal class RoundConfiguration : IEntityTypeConfiguration<Round>
{
    public void Configure(EntityTypeBuilder<Round> builder)
    {
        builder.ToTable("Rounds", DbSchemas.PokerAttack);

        builder.HasKey(x => x.Id);
        builder.Property(e => e.Id)
          .ValueGeneratedOnAdd()
          .UseIdentityColumn();
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(x => x.Game)
            .WithMany(x => x.Rounds);
        builder.HasMany(x => x.RoundScores)
            .WithOne(x => x.Round);
    }
}
