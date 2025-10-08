using ChefKnifeStudios.PokerAttack.Server.Data.Constants;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Configuration;

internal class UserRoundConfiguration : IEntityTypeConfiguration<UserRound>
{
    public void Configure(EntityTypeBuilder<UserRound> builder)
    {
        builder.ToTable("UserRounds", DbSchemas.PokerAttack);

        builder.HasKey(x => x.Id);
        builder.Property(e => e.Id)
          .ValueGeneratedOnAdd()
          .UseIdentityColumn();
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne(x => x.User)
            .WithMany(x => x.UserRounds);
        builder.HasOne(x => x.Round)
            .WithMany(x => x.UserRounds);
    }
}
