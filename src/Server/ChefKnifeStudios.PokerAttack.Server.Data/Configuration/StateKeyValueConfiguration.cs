using ChefKnifeStudios.PokerAttack.Server.Data.Constants;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Configuration;

internal class StateKeyValueConfiguration : IEntityTypeConfiguration<StateKeyValue>
{
    public void Configure(EntityTypeBuilder<StateKeyValue> builder)
    {
        builder.ToTable("state_kv", DbSchemas.PokerAttack);

        // Composite key: the interface scopes keys per TValue, so Key alone is not unique
        // across types (ActiveGame and GameStates are both keyed by game id today).
        builder.HasKey(x => new { x.Type, x.Key });

        builder.Property(x => x.Key).HasColumnName("key");
        builder.Property(x => x.Type).HasColumnName("type");
        builder.Property(x => x.Value).HasColumnName("value").HasColumnType("jsonb");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.Type).HasDatabaseName("ix_state_kv_type");
        builder.HasIndex(x => x.ExpiresAt)
            .HasDatabaseName("ix_state_kv_expires_at")
            .HasFilter("expires_at IS NOT NULL");
    }
}
