-- Script Date: 2026/06/13 13:41  - ErikEJ.SqlCeScripting version 3.5.2.103
-- Database information:
-- Database: C:\Users\Dean\source\repos\adam\src\Adam.CatalogBrowser\bin\Debug\net10.0\.adam\catalog.db
-- ServerVersion: 3.46.1
-- DatabaseSize: 25,922 MB
-- Created: 2026/06/13 11:37

-- User Table information:
-- Number of tables: 23
-- __EFMigrationsHistory: -1 row(s)
-- __EFMigrationsLock: -1 row(s)
-- AccessLogs: -1 row(s)
-- AssetCategories: -1 row(s)
-- AssetKeywords: -1 row(s)
-- Categories: -1 row(s)
-- Collections: -1 row(s)
-- digital_assets_fts: -1 row(s)
-- digital_assets_fts_config: -1 row(s)
-- digital_assets_fts_content: -1 row(s)
-- digital_assets_fts_data: -1 row(s)
-- digital_assets_fts_docsize: -1 row(s)
-- digital_assets_fts_idx: -1 row(s)
-- digital_assets_fts_map: -1 row(s)
-- DigitalAssets: -1 row(s)
-- Keywords: -1 row(s)
-- MetadataProfiles: -1 row(s)
-- ModeConfigurations: -1 row(s)
-- RatingInfos: -1 row(s)
-- RolePermissions: -1 row(s)
-- Roles: -1 row(s)
-- Users: -1 row(s)
-- WatchedFolders: -1 row(s)

SELECT 1;
PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
CREATE TABLE [WatchedFolders] (
  [Id] text NOT NULL
, [Path] text NOT NULL
, [IsEnabled] bigint NOT NULL
, [CreatedAt] text NOT NULL
, [ModifiedAt] text NOT NULL
, CONSTRAINT [sqlite_autoindex_WatchedFolders_1] PRIMARY KEY ([Id])
);
CREATE TABLE [Roles] (
  [Id] text NOT NULL
, [Name] text NOT NULL
, CONSTRAINT [sqlite_autoindex_Roles_1] PRIMARY KEY ([Id])
);
CREATE TABLE [Users] (
  [Id] text NOT NULL
, [Username] text NOT NULL
, [Email] text NOT NULL
, [PasswordHash] text NOT NULL
, [RoleId] text NOT NULL
, [IsActive] bigint NOT NULL
, [CreatedAt] text NOT NULL
, [LastLoginAt] text NULL
, CONSTRAINT [sqlite_autoindex_Users_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_Users_0_0] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [RolePermissions] (
  [RoleId] text NOT NULL
, [Permission] text NOT NULL
, CONSTRAINT [sqlite_autoindex_RolePermissions_1] PRIMARY KEY ([RoleId],[Permission])
, CONSTRAINT [FK_RolePermissions_0_0] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [ModeConfigurations] (
  [Id] text NOT NULL
, [Mode] text NOT NULL
, [DbProvider] text NOT NULL
, [ConnectionString] text NOT NULL
, [ServiceEndpoint] text NULL
, [ServiceInstalled] bigint NOT NULL
, [LastModified] text NOT NULL
, CONSTRAINT [sqlite_autoindex_ModeConfigurations_1] PRIMARY KEY ([Id])
);
CREATE TABLE [Keywords] (
  [Id] text NOT NULL
, [Name] text NOT NULL
, [NormalizedName] text NOT NULL
, [ParentId] text NULL
, [UsageCount] bigint NOT NULL
, CONSTRAINT [sqlite_autoindex_Keywords_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_Keywords_0_0] FOREIGN KEY ([ParentId]) REFERENCES [Keywords] ([Id]) ON DELETE RESTRICT ON UPDATE NO ACTION
);
CREATE TABLE [digital_assets_fts_map] (
  [fts_rowid] INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL
, [asset_id] text NOT NULL
);
CREATE TABLE [digital_assets_fts_idx] (
  [segid]  NOT NULL
, [term]  NOT NULL
, [pgno]  NULL
, CONSTRAINT [sqlite_autoindex_digital_assets_fts_idx_1] PRIMARY KEY ([segid],[term])
);
CREATE TABLE [digital_assets_fts_docsize] (
  [id] bigint NOT NULL
, [sz] image NULL
, CONSTRAINT [sqlite_master_PK_digital_assets_fts_docsize] PRIMARY KEY ([id])
);
CREATE TABLE [digital_assets_fts_data] (
  [id] bigint NOT NULL
, [block] image NULL
, CONSTRAINT [sqlite_master_PK_digital_assets_fts_data] PRIMARY KEY ([id])
);
CREATE TABLE [digital_assets_fts_content] (
  [id] bigint NOT NULL
, [c0]  NULL
, [c1]  NULL
, [c2]  NULL
, [c3]  NULL
, CONSTRAINT [sqlite_master_PK_digital_assets_fts_content] PRIMARY KEY ([id])
);
CREATE TABLE [digital_assets_fts_config] (
  [k]  NOT NULL
, [v]  NULL
, CONSTRAINT [sqlite_autoindex_digital_assets_fts_config_1] PRIMARY KEY ([k])
);
CREATE TABLE [digital_assets_fts] (
  [Title]  NULL
, [Description]  NULL
, [FileName]  NULL
, [Keywords]  NULL
);
CREATE TABLE [Collections] (
  [Id] text NOT NULL
, [Name] text NOT NULL
, [Description] text NULL
, [ParentId] text NULL
, [CreatedAt] text NOT NULL
, [ModifiedAt] text NOT NULL
, CONSTRAINT [sqlite_autoindex_Collections_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_Collections_0_0] FOREIGN KEY ([ParentId]) REFERENCES [Collections] ([Id]) ON DELETE RESTRICT ON UPDATE NO ACTION
);
CREATE TABLE [DigitalAssets] (
  [Id] text NOT NULL
, [FileName] text NOT NULL
, [FileExtension] text NOT NULL
, [MimeType] text NOT NULL
, [FileSize] bigint NOT NULL
, [ChecksumSha256] text NOT NULL
, [StoragePath] text NOT NULL
, [OriginalPath] text NOT NULL
, [Title] text NOT NULL
, [Description] text NULL
, [Type] bigint NOT NULL
, [Width] bigint NULL
, [Height] bigint NULL
, [Duration] real NULL
, [CollectionId] text NULL
, [UploadedByUserId] text NULL
, [IsDeleted] bigint DEFAULT (0) NOT NULL
, [Version] bigint DEFAULT (1) NOT NULL
, [Rating] bigint NOT NULL
, [Label] bigint NOT NULL
, [Flag] bigint NOT NULL
, [GpsLatitude] real NULL
, [GpsLongitude] real NULL
, [Copyright] text NULL
, [Orientation] bigint NOT NULL
, [CreatedAt] text NOT NULL
, [ModifiedAt] text NOT NULL
, CONSTRAINT [sqlite_autoindex_DigitalAssets_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_DigitalAssets_0_0] FOREIGN KEY ([CollectionId]) REFERENCES [Collections] ([Id]) ON DELETE NO ACTION ON UPDATE NO ACTION
);
CREATE TABLE [RatingInfos] (
  [Id] text NOT NULL
, [DigitalAssetId] text NOT NULL
, [Stars] bigint NOT NULL
, [ColorLabel] bigint NOT NULL
, [Flag] bigint NOT NULL
, CONSTRAINT [sqlite_autoindex_RatingInfos_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_RatingInfos_0_0] FOREIGN KEY ([DigitalAssetId]) REFERENCES [DigitalAssets] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [MetadataProfiles] (
  [Id] text NOT NULL
, [DigitalAssetId] text NOT NULL
, [CameraMake] text NULL
, [CameraModel] text NULL
, [LensModel] text NULL
, [FocalLength] real NULL
, [Aperture] real NULL
, [ExposureTime] text NULL
, [Iso] bigint NULL
, [Flash] bigint NULL
, [GpsLatitude] real NULL
, [GpsLongitude] real NULL
, [GpsAltitude] real NULL
, [DateTaken] text NULL
, [Orientation] text NULL
, [Rating] bigint NULL
, [Creator] text NULL
, [Copyright] text NULL
, [UsageTerms] text NULL
, [ContactInfo] text NULL
, [City] text NULL
, [State] text NULL
, [Country] text NULL
, [Headline] text NULL
, [Description] text NULL
, [Title] text NULL
, CONSTRAINT [sqlite_autoindex_MetadataProfiles_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_MetadataProfiles_0_0] FOREIGN KEY ([DigitalAssetId]) REFERENCES [DigitalAssets] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [Categories] (
  [Id] text NOT NULL
, [Name] text NOT NULL
, [NormalizedName] text NOT NULL
, [Description] text NULL
, [ParentId] text NULL
, CONSTRAINT [sqlite_autoindex_Categories_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_Categories_0_0] FOREIGN KEY ([ParentId]) REFERENCES [Categories] ([Id]) ON DELETE RESTRICT ON UPDATE NO ACTION
);
CREATE TABLE [AssetKeywords] (
  [DigitalAssetsId] text NOT NULL
, [KeywordsId] text NOT NULL
, CONSTRAINT [sqlite_autoindex_AssetKeywords_1] PRIMARY KEY ([DigitalAssetsId],[KeywordsId])
, CONSTRAINT [FK_AssetKeywords_0_0] FOREIGN KEY ([KeywordsId]) REFERENCES [Keywords] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
, CONSTRAINT [FK_AssetKeywords_1_0] FOREIGN KEY ([DigitalAssetsId]) REFERENCES [DigitalAssets] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [AssetCategories] (
  [DigitalAssetsId] text NOT NULL
, [CategoriesId] text NOT NULL
, CONSTRAINT [sqlite_autoindex_AssetCategories_1] PRIMARY KEY ([DigitalAssetsId],[CategoriesId])
, CONSTRAINT [FK_AssetCategories_0_0] FOREIGN KEY ([DigitalAssetsId]) REFERENCES [DigitalAssets] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
, CONSTRAINT [FK_AssetCategories_1_0] FOREIGN KEY ([CategoriesId]) REFERENCES [Categories] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [AccessLogs] (
  [Id] text NOT NULL
, [UserId] text NOT NULL
, [Action] text NOT NULL
, [EntityType] text NOT NULL
, [EntityId] text NULL
, [Details] text NULL
, [IpAddress] text NULL
, [Timestamp] text NOT NULL
, CONSTRAINT [sqlite_autoindex_AccessLogs_1] PRIMARY KEY ([Id])
, CONSTRAINT [FK_AccessLogs_0_0] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE ON UPDATE NO ACTION
);
CREATE TABLE [__EFMigrationsLock] (
  [Id] bigint NOT NULL
, [Timestamp] text NOT NULL
, CONSTRAINT [sqlite_master_PK___EFMigrationsLock] PRIMARY KEY ([Id])
);
CREATE TABLE [__EFMigrationsHistory] (
  [MigrationId] text NOT NULL
, [ProductVersion] text NOT NULL
, CONSTRAINT [sqlite_autoindex___EFMigrationsHistory_1] PRIMARY KEY ([MigrationId])
);
CREATE UNIQUE INDEX [IX_WatchedFolders_Path] ON [WatchedFolders] ([Path] ASC);
CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name] ASC);
CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username] ASC);
CREATE INDEX [IX_Users_RoleId] ON [Users] ([RoleId] ASC);
CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email] ASC);
CREATE INDEX [IX_Keywords_ParentId] ON [Keywords] ([ParentId] ASC);
CREATE UNIQUE INDEX [IX_Keywords_NormalizedName_ParentId] ON [Keywords] ([NormalizedName] ASC,[ParentId] ASC);
CREATE UNIQUE INDEX [sqlite_autoindex_digital_assets_fts_map_1] ON [digital_assets_fts_map] ([asset_id] ASC);
CREATE INDEX [IX_Collections_ParentId] ON [Collections] ([ParentId] ASC);
CREATE UNIQUE INDEX [IX_Collections_Name_ParentId] ON [Collections] ([Name] ASC,[ParentId] ASC);
CREATE INDEX [IX_DigitalAssets_Type_CreatedAt] ON [DigitalAssets] ([Type] ASC,[CreatedAt] ASC);
CREATE INDEX [IX_DigitalAssets_Type] ON [DigitalAssets] ([Type] ASC);
CREATE INDEX [IX_DigitalAssets_StoragePath] ON [DigitalAssets] ([StoragePath] ASC);
CREATE INDEX [IX_DigitalAssets_ModifiedAt] ON [DigitalAssets] ([ModifiedAt] ASC);
CREATE INDEX [IX_DigitalAssets_MimeType] ON [DigitalAssets] ([MimeType] ASC);
CREATE INDEX [IX_DigitalAssets_FileSize] ON [DigitalAssets] ([FileSize] ASC);
CREATE INDEX [IX_DigitalAssets_FileName] ON [DigitalAssets] ([FileName] ASC);
CREATE INDEX [IX_DigitalAssets_CreatedAt] ON [DigitalAssets] ([CreatedAt] ASC);
CREATE INDEX [IX_DigitalAssets_CollectionId] ON [DigitalAssets] ([CollectionId] ASC);
CREATE UNIQUE INDEX [IX_DigitalAssets_ChecksumSha256] ON [DigitalAssets] ([ChecksumSha256] ASC);
CREATE UNIQUE INDEX [IX_RatingInfos_DigitalAssetId] ON [RatingInfos] ([DigitalAssetId] ASC);
CREATE INDEX [IX_MetadataProfiles_Rating] ON [MetadataProfiles] ([Rating] ASC);
CREATE UNIQUE INDEX [IX_MetadataProfiles_DigitalAssetId] ON [MetadataProfiles] ([DigitalAssetId] ASC);
CREATE INDEX [IX_MetadataProfiles_DateTaken] ON [MetadataProfiles] ([DateTaken] ASC);
CREATE INDEX [IX_Categories_ParentId] ON [Categories] ([ParentId] ASC);
CREATE UNIQUE INDEX [IX_Categories_NormalizedName_ParentId] ON [Categories] ([NormalizedName] ASC,[ParentId] ASC);
CREATE INDEX [IX_AssetKeywords_KeywordId] ON [AssetKeywords] ([KeywordsId] ASC);
CREATE INDEX [IX_AssetKeywords_AssetId] ON [AssetKeywords] ([DigitalAssetsId] ASC);
CREATE INDEX [IX_AssetCategories_CategoryId] ON [AssetCategories] ([CategoriesId] ASC);
CREATE INDEX [IX_AssetCategories_AssetId] ON [AssetCategories] ([DigitalAssetsId] ASC);
CREATE INDEX [IX_AccessLogs_UserId] ON [AccessLogs] ([UserId] ASC);
CREATE INDEX [IX_AccessLogs_Timestamp] ON [AccessLogs] ([Timestamp] ASC);
CREATE TRIGGER trg_fts_da_insert
            AFTER INSERT ON DigitalAssets
            BEGIN
                INSERT INTO digital_assets_fts_map(asset_id) VALUES (new.Id);
                INSERT INTO digital_assets_fts(rowid, Title, Description, FileName, Keywords)
                VALUES (
                    last_insert_rowid(),
                    new.Title,
                    new.Description,
                    new.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = new.Id
                    ), '')
                );
            END;
CREATE TRIGGER trg_fts_da_update
            AFTER UPDATE ON DigitalAssets
            BEGIN
                DELETE FROM digital_assets_fts WHERE rowid = (
                    SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = old.Id
                );
                INSERT INTO digital_assets_fts(rowid, Title, Description, FileName, Keywords)
                VALUES (
                    (SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = new.Id),
                    new.Title,
                    new.Description,
                    new.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = new.Id
                    ), '')
                );
            END;
CREATE TRIGGER trg_fts_da_delete
            AFTER DELETE ON DigitalAssets
            BEGIN
                DELETE FROM digital_assets_fts WHERE rowid = (
                    SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = old.Id
                );
                DELETE FROM digital_assets_fts_map WHERE asset_id = old.Id;
            END;
CREATE TRIGGER trg_fts_ak_insert
            AFTER INSERT ON AssetKeywords
            BEGIN
                DELETE FROM digital_assets_fts WHERE rowid = (
                    SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = new.DigitalAssetsId
                );
                INSERT INTO digital_assets_fts(rowid, Title, Description, FileName, Keywords)
                SELECT
                    (SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = da.Id),
                    da.Title, da.Description, da.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = da.Id
                    ), '')
                FROM DigitalAssets da WHERE da.Id = new.DigitalAssetsId;
            END;
CREATE TRIGGER trg_fts_ak_delete
            AFTER DELETE ON AssetKeywords
            BEGIN
                DELETE FROM digital_assets_fts WHERE rowid = (
                    SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = old.DigitalAssetsId
                );
                INSERT INTO digital_assets_fts(rowid, Title, Description, FileName, Keywords)
                SELECT
                    (SELECT fts_rowid FROM digital_assets_fts_map WHERE asset_id = da.Id),
                    da.Title, da.Description, da.FileName,
                    COALESCE((
                        SELECT GROUP_CONCAT(k.Name, ' ')
                        FROM AssetKeywords ak
                        JOIN Keywords k ON ak.KeywordsId = k.Id
                        WHERE ak.DigitalAssetsId = da.Id
                    ), '')
                FROM DigitalAssets da WHERE da.Id = old.DigitalAssetsId;
            END;
CREATE TRIGGER [fki_Users_RoleId_Roles_Id] BEFORE Insert ON [Users] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table Users violates foreign key constraint FK_Users_0_0') WHERE NOT EXISTS (SELECT * FROM Roles WHERE  Id = NEW.RoleId); END;
CREATE TRIGGER [fku_Users_RoleId_Roles_Id] BEFORE Update ON [Users] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table Users violates foreign key constraint FK_Users_0_0') WHERE NOT EXISTS (SELECT * FROM Roles WHERE  Id = NEW.RoleId); END;
CREATE TRIGGER [fki_RolePermissions_RoleId_Roles_Id] BEFORE Insert ON [RolePermissions] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table RolePermissions violates foreign key constraint FK_RolePermissions_0_0') WHERE NOT EXISTS (SELECT * FROM Roles WHERE  Id = NEW.RoleId); END;
CREATE TRIGGER [fku_RolePermissions_RoleId_Roles_Id] BEFORE Update ON [RolePermissions] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table RolePermissions violates foreign key constraint FK_RolePermissions_0_0') WHERE NOT EXISTS (SELECT * FROM Roles WHERE  Id = NEW.RoleId); END;
CREATE TRIGGER [fki_Keywords_ParentId_Keywords_Id] BEFORE Insert ON [Keywords] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table Keywords violates foreign key constraint FK_Keywords_0_0') WHERE NEW.ParentId IS NOT NULL AND NOT EXISTS (SELECT * FROM Keywords WHERE  Id = NEW.ParentId); END;
CREATE TRIGGER [fku_Keywords_ParentId_Keywords_Id] BEFORE Update ON [Keywords] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table Keywords violates foreign key constraint FK_Keywords_0_0') WHERE NEW.ParentId IS NOT NULL AND NOT EXISTS (SELECT * FROM Keywords WHERE  Id = NEW.ParentId); END;
CREATE TRIGGER [fki_Collections_ParentId_Collections_Id] BEFORE Insert ON [Collections] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table Collections violates foreign key constraint FK_Collections_0_0') WHERE NEW.ParentId IS NOT NULL AND NOT EXISTS (SELECT * FROM Collections WHERE  Id = NEW.ParentId); END;
CREATE TRIGGER [fku_Collections_ParentId_Collections_Id] BEFORE Update ON [Collections] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table Collections violates foreign key constraint FK_Collections_0_0') WHERE NEW.ParentId IS NOT NULL AND NOT EXISTS (SELECT * FROM Collections WHERE  Id = NEW.ParentId); END;
CREATE TRIGGER [fki_DigitalAssets_CollectionId_Collections_Id] BEFORE Insert ON [DigitalAssets] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table DigitalAssets violates foreign key constraint FK_DigitalAssets_0_0') WHERE NEW.CollectionId IS NOT NULL AND NOT EXISTS (SELECT * FROM Collections WHERE  Id = NEW.CollectionId); END;
CREATE TRIGGER [fku_DigitalAssets_CollectionId_Collections_Id] BEFORE Update ON [DigitalAssets] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table DigitalAssets violates foreign key constraint FK_DigitalAssets_0_0') WHERE NEW.CollectionId IS NOT NULL AND NOT EXISTS (SELECT * FROM Collections WHERE  Id = NEW.CollectionId); END;
CREATE TRIGGER [fki_RatingInfos_DigitalAssetId_DigitalAssets_Id] BEFORE Insert ON [RatingInfos] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table RatingInfos violates foreign key constraint FK_RatingInfos_0_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetId); END;
CREATE TRIGGER [fku_RatingInfos_DigitalAssetId_DigitalAssets_Id] BEFORE Update ON [RatingInfos] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table RatingInfos violates foreign key constraint FK_RatingInfos_0_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetId); END;
CREATE TRIGGER [fki_MetadataProfiles_DigitalAssetId_DigitalAssets_Id] BEFORE Insert ON [MetadataProfiles] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table MetadataProfiles violates foreign key constraint FK_MetadataProfiles_0_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetId); END;
CREATE TRIGGER [fku_MetadataProfiles_DigitalAssetId_DigitalAssets_Id] BEFORE Update ON [MetadataProfiles] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table MetadataProfiles violates foreign key constraint FK_MetadataProfiles_0_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetId); END;
CREATE TRIGGER [fki_Categories_ParentId_Categories_Id] BEFORE Insert ON [Categories] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table Categories violates foreign key constraint FK_Categories_0_0') WHERE NEW.ParentId IS NOT NULL AND NOT EXISTS (SELECT * FROM Categories WHERE  Id = NEW.ParentId); END;
CREATE TRIGGER [fku_Categories_ParentId_Categories_Id] BEFORE Update ON [Categories] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table Categories violates foreign key constraint FK_Categories_0_0') WHERE NEW.ParentId IS NOT NULL AND NOT EXISTS (SELECT * FROM Categories WHERE  Id = NEW.ParentId); END;
CREATE TRIGGER [fki_AssetKeywords_KeywordsId_Keywords_Id] BEFORE Insert ON [AssetKeywords] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table AssetKeywords violates foreign key constraint FK_AssetKeywords_0_0') WHERE NOT EXISTS (SELECT * FROM Keywords WHERE  Id = NEW.KeywordsId); END;
CREATE TRIGGER [fku_AssetKeywords_KeywordsId_Keywords_Id] BEFORE Update ON [AssetKeywords] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table AssetKeywords violates foreign key constraint FK_AssetKeywords_0_0') WHERE NOT EXISTS (SELECT * FROM Keywords WHERE  Id = NEW.KeywordsId); END;
CREATE TRIGGER [fki_AssetKeywords_DigitalAssetsId_DigitalAssets_Id] BEFORE Insert ON [AssetKeywords] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table AssetKeywords violates foreign key constraint FK_AssetKeywords_1_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetsId); END;
CREATE TRIGGER [fku_AssetKeywords_DigitalAssetsId_DigitalAssets_Id] BEFORE Update ON [AssetKeywords] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table AssetKeywords violates foreign key constraint FK_AssetKeywords_1_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetsId); END;
CREATE TRIGGER [fki_AssetCategories_DigitalAssetsId_DigitalAssets_Id] BEFORE Insert ON [AssetCategories] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table AssetCategories violates foreign key constraint FK_AssetCategories_0_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetsId); END;
CREATE TRIGGER [fku_AssetCategories_DigitalAssetsId_DigitalAssets_Id] BEFORE Update ON [AssetCategories] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table AssetCategories violates foreign key constraint FK_AssetCategories_0_0') WHERE NOT EXISTS (SELECT * FROM DigitalAssets WHERE  Id = NEW.DigitalAssetsId); END;
CREATE TRIGGER [fki_AssetCategories_CategoriesId_Categories_Id] BEFORE Insert ON [AssetCategories] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table AssetCategories violates foreign key constraint FK_AssetCategories_1_0') WHERE NOT EXISTS (SELECT * FROM Categories WHERE  Id = NEW.CategoriesId); END;
CREATE TRIGGER [fku_AssetCategories_CategoriesId_Categories_Id] BEFORE Update ON [AssetCategories] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table AssetCategories violates foreign key constraint FK_AssetCategories_1_0') WHERE NOT EXISTS (SELECT * FROM Categories WHERE  Id = NEW.CategoriesId); END;
CREATE TRIGGER [fki_AccessLogs_UserId_Users_Id] BEFORE Insert ON [AccessLogs] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Insert on table AccessLogs violates foreign key constraint FK_AccessLogs_0_0') WHERE NOT EXISTS (SELECT * FROM Users WHERE  Id = NEW.UserId); END;
CREATE TRIGGER [fku_AccessLogs_UserId_Users_Id] BEFORE Update ON [AccessLogs] FOR EACH ROW BEGIN SELECT RAISE(ROLLBACK, 'Update on table AccessLogs violates foreign key constraint FK_AccessLogs_0_0') WHERE NOT EXISTS (SELECT * FROM Users WHERE  Id = NEW.UserId); END;
COMMIT;

