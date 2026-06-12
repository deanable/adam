using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Adam.Shared.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_Collections_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Keywords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Keywords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Keywords_Keywords_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Keywords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ModeConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DbProvider = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ConnectionString = table.Column<string>(type: "TEXT", nullable: false),
                    ServiceEndpoint = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ServiceInstalled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModified = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModeConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Permissions = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchedFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedFolders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DigitalAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    FileExtension = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalPath = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Duration = table.Column<double>(type: "REAL", nullable: true),
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<int>(type: "INTEGER", nullable: false),
                    Flag = table.Column<int>(type: "INTEGER", nullable: false),
                    GpsLatitude = table.Column<double>(type: "REAL", nullable: true),
                    GpsLongitude = table.Column<double>(type: "REAL", nullable: true),
                    Copyright = table.Column<string>(type: "TEXT", nullable: true),
                    Orientation = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigitalAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigitalAssets_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetCategories",
                columns: table => new
                {
                    DigitalAssetsId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoriesId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetCategories", x => new { x.DigitalAssetsId, x.CategoriesId });
                    table.ForeignKey(
                        name: "FK_AssetCategories_Categories_CategoriesId",
                        column: x => x.CategoriesId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetCategories_DigitalAssets_DigitalAssetsId",
                        column: x => x.DigitalAssetsId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssetKeywords",
                columns: table => new
                {
                    DigitalAssetsId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KeywordsId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetKeywords", x => new { x.DigitalAssetsId, x.KeywordsId });
                    table.ForeignKey(
                        name: "FK_AssetKeywords_DigitalAssets_DigitalAssetsId",
                        column: x => x.DigitalAssetsId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssetKeywords_Keywords_KeywordsId",
                        column: x => x.KeywordsId,
                        principalTable: "Keywords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetadataProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DigitalAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CameraMake = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CameraModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LensModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    FocalLength = table.Column<double>(type: "REAL", nullable: true),
                    Aperture = table.Column<double>(type: "REAL", nullable: true),
                    ExposureTime = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Iso = table.Column<int>(type: "INTEGER", nullable: true),
                    Flash = table.Column<bool>(type: "INTEGER", nullable: true),
                    GpsLatitude = table.Column<double>(type: "REAL", nullable: true),
                    GpsLongitude = table.Column<double>(type: "REAL", nullable: true),
                    GpsAltitude = table.Column<double>(type: "REAL", nullable: true),
                    DateTaken = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Orientation = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Rating = table.Column<int>(type: "INTEGER", nullable: true),
                    Creator = table.Column<string>(type: "TEXT", nullable: true),
                    Copyright = table.Column<string>(type: "TEXT", nullable: true),
                    UsageTerms = table.Column<string>(type: "TEXT", nullable: true),
                    ContactInfo = table.Column<string>(type: "TEXT", nullable: true),
                    City = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<string>(type: "TEXT", nullable: true),
                    Country = table.Column<string>(type: "TEXT", nullable: true),
                    Headline = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataProfiles_DigitalAssets_DigitalAssetId",
                        column: x => x.DigitalAssetId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RatingInfos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DigitalAssetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    ColorLabel = table.Column<int>(type: "INTEGER", nullable: false),
                    Flag = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatingInfos_DigitalAssets_DigitalAssetId",
                        column: x => x.DigitalAssetId,
                        principalTable: "DigitalAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Name", "Permissions" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "Viewer", "asset:read,collection:read" },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "Editor", "asset:read,asset:create,asset:update,collection:read,collection:update" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "Administrator", "asset:*,collection:*,user:*,role:*,audit:read" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_Timestamp",
                table: "AccessLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_UserId",
                table: "AccessLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_AssetId",
                table: "AssetCategories",
                column: "DigitalAssetsId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetCategories_CategoryId",
                table: "AssetCategories",
                column: "CategoriesId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetKeywords_AssetId",
                table: "AssetKeywords",
                column: "DigitalAssetsId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetKeywords_KeywordId",
                table: "AssetKeywords",
                column: "KeywordsId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_NormalizedName_ParentId",
                table: "Categories",
                columns: new[] { "NormalizedName", "ParentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId",
                table: "Categories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Name_ParentId",
                table: "Collections",
                columns: new[] { "Name", "ParentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_ParentId",
                table: "Collections",
                column: "ParentId");

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

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_CreatedAt",
                table: "DigitalAssets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_FileName",
                table: "DigitalAssets",
                column: "FileName");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_FileSize",
                table: "DigitalAssets",
                column: "FileSize");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_MimeType",
                table: "DigitalAssets",
                column: "MimeType");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_ModifiedAt",
                table: "DigitalAssets",
                column: "ModifiedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_StoragePath",
                table: "DigitalAssets",
                column: "StoragePath");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_Type",
                table: "DigitalAssets",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_DigitalAssets_Type_CreatedAt",
                table: "DigitalAssets",
                columns: new[] { "Type", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Keywords_NormalizedName_ParentId",
                table: "Keywords",
                columns: new[] { "NormalizedName", "ParentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Keywords_ParentId",
                table: "Keywords",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProfiles_DateTaken",
                table: "MetadataProfiles",
                column: "DateTaken");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProfiles_DigitalAssetId",
                table: "MetadataProfiles",
                column: "DigitalAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataProfiles_Rating",
                table: "MetadataProfiles",
                column: "Rating");

            migrationBuilder.CreateIndex(
                name: "IX_RatingInfos_DigitalAssetId",
                table: "RatingInfos",
                column: "DigitalAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedFolders_Path",
                table: "WatchedFolders",
                column: "Path",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.DropTable(
                name: "AssetCategories");

            migrationBuilder.DropTable(
                name: "AssetKeywords");

            migrationBuilder.DropTable(
                name: "MetadataProfiles");

            migrationBuilder.DropTable(
                name: "ModeConfigurations");

            migrationBuilder.DropTable(
                name: "RatingInfos");

            migrationBuilder.DropTable(
                name: "WatchedFolders");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Keywords");

            migrationBuilder.DropTable(
                name: "DigitalAssets");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Collections");
        }
    }
}
