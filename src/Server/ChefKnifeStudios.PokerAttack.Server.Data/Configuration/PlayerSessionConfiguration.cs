using ChefKnifeStudios.PokerAttack.Server.Data.Constants;
using ChefKnifeStudios.PokerAttack.Server.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ChefKnifeStudios.PokerAttack.Server.Data.Configuration;

internal class PlayerSessionConfiguration : IEntityTypeConfiguration<PlayerSessionEntity>
{
    public void Configure(EntityTypeBuilder<PlayerSessionEntity> builder)
    {
        builder.ToTable("player_session", DbSchemas.PokerAttack);

        builder.HasKey(x => x.PlayerId);

        builder.Property(x => x.PlayerId).HasColumnName("player_id");
        builder.Property(x => x.GameId).HasColumnName("game_id");
        builder.Property(x => x.LobbyId).HasColumnName("lobby_id");
        builder.Property(x => x.Connected).HasColumnName("connected");
        builder.Property(x => x.DisconnectedAt).HasColumnName("disconnected_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Replaces the GetAllAsync() linear scans in FindPlayerLobbyAsync/FindPlayerGameAsync.
        builder.HasIndex(x => x.GameId).HasDatabaseName("ix_player_session_game_id");
        builder.HasIndex(x => x.DisconnectedAt)
            .HasDatabaseName("ix_player_session_disconnected_at")
            .HasFilter("disconnected_at IS NOT NULL");
    }
}
