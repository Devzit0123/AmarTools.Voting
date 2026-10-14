using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmarTools.Voting.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueSlugIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_VotingPrograms_Slug",
                table: "VotingPrograms",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VotingPrograms_Slug",
                table: "VotingPrograms");
        }
    }
}
