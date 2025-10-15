using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChefKnifeStudios.PokerAttack.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Update_Game_HostPlayerClientId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HostPlayerClientId",
                schema: "PokerAttack",
                table: "Games",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HostPlayerClientId",
                schema: "PokerAttack",
                table: "Games");
        }
    }
}
