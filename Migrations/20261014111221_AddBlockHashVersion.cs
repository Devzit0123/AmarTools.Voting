using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmarTools.Voting.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockHashVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HashVersion",
                table: "BlockchainVotes",
                type: "integer",
                nullable: false,
                // Blocks created before this migration used the legacy (v1) formula.
                defaultValue: 1);

            // D17: stop keeping voters' IP addresses next to their ballots.
            migrationBuilder.Sql("UPDATE \"Votes\" SET \"IpAddress\" = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HashVersion",
                table: "BlockchainVotes");
        }
    }
}
