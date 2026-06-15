using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adam.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAiGenerated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAiGenerated",
                table: "Keywords",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAiGenerated",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAiGenerated",
                table: "Keywords");

            migrationBuilder.DropColumn(
                name: "IsAiGenerated",
                table: "Categories");
        }
    }
}
