using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adam.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddSortOrderColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_ChecksumSha256",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_CollectionId",
                table: "DigitalAssets");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "DigitalAssets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsSmart",
                table: "Collections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAutoRefreshedAt",
                table: "Collections",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmartQueryJson",
                table: "Collections",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssetEmbeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TextEmbedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ImageEmbedding = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ModelVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ComputedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetEmbeddings_DigitalAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QueryText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FiltersJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedSearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavedSearches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SearchHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueryText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    FiltersJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsSemantic = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistoryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchHistoryEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_ChecksumSha256_IsDeleted",
                table: "DigitalAssets",
                columns: new[] { "ChecksumSha256", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_CollectionId_SortOrder",
                table: "DigitalAssets",
                columns: new[] { "CollectionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_CreatedAt_Id",
                table: "DigitalAssets",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_FileName_CreatedAt_Id",
                table: "DigitalAssets",
                columns: new[] { "FileName", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_FileSize_Id",
                table: "DigitalAssets",
                columns: new[] { "FileSize", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_MimeType_CreatedAt",
                table: "DigitalAssets",
                columns: new[] { "MimeType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_MimeType_FileName",
                table: "DigitalAssets",
                columns: new[] { "MimeType", "FileName" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_Rating_CreatedAt",
                table: "DigitalAssets",
                columns: new[] { "Rating", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_Rating_CreatedAt_Id",
                table: "DigitalAssets",
                columns: new[] { "Rating", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_SortOrder",
                table: "DigitalAssets",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_Type_CreatedAt_Id",
                table: "DigitalAssets",
                columns: new[] { "Type", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetEmbeddings_AssetId",
                table: "AssetEmbeddings",
                column: "AssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_UserId",
                table: "SavedSearches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SavedSearches_UserId_Name",
                table: "SavedSearches",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistoryEntries_ExecutedAt",
                table: "SearchHistoryEntries",
                column: "ExecutedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistoryEntries_UserId",
                table: "SearchHistoryEntries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetEmbeddings");

            migrationBuilder.DropTable(
                name: "SavedSearches");

            migrationBuilder.DropTable(
                name: "SearchHistoryEntries");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_ChecksumSha256_IsDeleted",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_CollectionId_SortOrder",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_CreatedAt_Id",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_FileName_CreatedAt_Id",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_FileSize_Id",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_MimeType_CreatedAt",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_MimeType_FileName",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_Rating_CreatedAt",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_Rating_CreatedAt_Id",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_SortOrder",
                table: "DigitalAssets");

            migrationBuilder.DropIndex(
                name: "IX_DigitalAssets_Type_CreatedAt_Id",
                table: "DigitalAssets");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "DigitalAssets");

            migrationBuilder.DropColumn(
                name: "IsSmart",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "LastAutoRefreshedAt",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "SmartQueryJson",
                table: "Collections");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_ChecksumSha256",
                table: "DigitalAssets",
                column: "ChecksumSha256",
                unique: true,
                filter: "NOT IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_CollectionId",
                table: "DigitalAssets",
                column: "CollectionId");
        }
    }
}
