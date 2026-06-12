using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Adam.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddRolePermissionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "Roles");

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Permission = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "RolePermissions",
                columns: new[] { "Permission", "RoleId" },
                values: new object[,]
                {
                    { "asset:read", new Guid("00000000-0000-0000-0000-000000000001") },
                    { "collection:read", new Guid("00000000-0000-0000-0000-000000000001") },
                    { "asset:create", new Guid("00000000-0000-0000-0000-000000000002") },
                    { "asset:read", new Guid("00000000-0000-0000-0000-000000000002") },
                    { "asset:update", new Guid("00000000-0000-0000-0000-000000000002") },
                    { "collection:read", new Guid("00000000-0000-0000-0000-000000000002") },
                    { "collection:update", new Guid("00000000-0000-0000-0000-000000000002") },
                    { "asset:*", new Guid("00000000-0000-0000-0000-000000000003") },
                    { "audit:read", new Guid("00000000-0000-0000-0000-000000000003") },
                    { "collection:*", new Guid("00000000-0000-0000-0000-000000000003") },
                    { "role:*", new Guid("00000000-0000-0000-0000-000000000003") },
                    { "user:*", new Guid("00000000-0000-0000-0000-000000000003") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.AddColumn<string>(
                name: "Permissions",
                table: "Roles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"),
                column: "Permissions",
                value: "asset:read,collection:read");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000002"),
                column: "Permissions",
                value: "asset:read,asset:create,asset:update,collection:read,collection:update");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000003"),
                column: "Permissions",
                value: "asset:*,collection:*,user:*,role:*,audit:read");
        }
    }
}
