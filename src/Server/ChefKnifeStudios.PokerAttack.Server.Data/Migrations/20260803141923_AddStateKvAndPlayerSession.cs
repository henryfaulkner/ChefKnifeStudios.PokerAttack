using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.PokerAttack.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStateKvAndPlayerSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_session",
                schema: "PokerAttack",
                columns: table => new
                {
                    player_id = table.Column<string>(type: "text", nullable: false),
                    game_id = table.Column<string>(type: "text", nullable: true),
                    lobby_id = table.Column<string>(type: "text", nullable: true),
                    connected = table.Column<bool>(type: "boolean", nullable: false),
                    disconnected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_session", x => x.player_id);
                });

            migrationBuilder.CreateTable(
                name: "state_kv",
                schema: "PokerAttack",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_state_kv", x => new { x.type, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_session_disconnected_at",
                schema: "PokerAttack",
                table: "player_session",
                column: "disconnected_at",
                filter: "disconnected_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_player_session_game_id",
                schema: "PokerAttack",
                table: "player_session",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_state_kv_expires_at",
                schema: "PokerAttack",
                table: "state_kv",
                column: "expires_at",
                filter: "expires_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_state_kv_type",
                schema: "PokerAttack",
                table: "state_kv",
                column: "type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_session",
                schema: "PokerAttack");

            migrationBuilder.DropTable(
                name: "state_kv",
                schema: "PokerAttack");
        }
    }
}
