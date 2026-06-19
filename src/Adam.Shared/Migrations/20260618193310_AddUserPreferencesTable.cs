using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Adam.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferencesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ThumbnailImage = table.Column<byte[]>(type: "BLOB", nullable: true),
                    CentroidEmbedding = table.Column<byte[]>(type: "BLOB", nullable: true),
                    EmbeddingModelVersion = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchClickLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueryText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    NormalizedQuery = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ClickedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DwellTimeMs = table.Column<int>(type: "INTEGER", nullable: false),
                    RankPosition = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchClickLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchClickLogs_DigitalAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SearchClickLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssetFaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FaceEmbedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    BoundingBoxJson = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DetectionConfidence = table.Column<float>(type: "REAL", nullable: false),
                    MatchingConfidence = table.Column<float>(type: "REAL", nullable: false),
                    IsAutoAssigned = table.Column<bool>(type: "INTEGER", nullable: false),
                    ThumbnailImage = table.Column<byte[]>(type: "BLOB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetFaces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetFaces_DigitalAssets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetFaces_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetFaces_AssetId",
                table: "AssetFaces",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetFaces_DetectionConfidence",
                table: "AssetFaces",
                column: "DetectionConfidence");

            migrationBuilder.CreateIndex(
                name: "IX_AssetFaces_PersonId",
                table: "AssetFaces",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_CreatedAt",
                table: "Persons",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Persons_Name",
                table: "Persons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickLogs_AssetId_NormalizedQuery",
                table: "SearchClickLogs",
                columns: new[] { "AssetId", "NormalizedQuery" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickLogs_NormalizedQuery_ClickedAt",
                table: "SearchClickLogs",
                columns: new[] { "NormalizedQuery", "ClickedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchClickLogs_UserId",
                table: "SearchClickLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId_Key",
                table: "UserPreferences",
                columns: new[] { "UserId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetFaces");

            migrationBuilder.DropTable(
                name: "SearchClickLogs");

            migrationBuilder.DropTable(
                name: "UserPreferences");

            migrationBuilder.DropTable(
                name: "Persons");
        }
    }
}
