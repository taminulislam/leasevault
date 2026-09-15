-- LeaseVault: SQL Server / Azure SQL full-text search setup
-- Run once against the LeaseVault database (requires ALTER permission; Azure SQL supports full-text
-- search on all service tiers). The application's SqlServerFullTextSearchService queries this index
-- with CONTAINSTABLE / FREETEXTTABLE over Title, Description and TagsText.

IF (SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')) <> 1
BEGIN
    RAISERROR('Full-text search is not installed on this SQL Server instance.', 16, 1);
    RETURN;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'LeaseVaultCatalog')
    CREATE FULLTEXT CATALOG LeaseVaultCatalog AS DEFAULT;
GO

-- EF Core names the clustered primary key PK_Documents.
IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('dbo.Documents'))
    CREATE FULLTEXT INDEX ON dbo.Documents
    (
        Title       LANGUAGE 1033,
        Description LANGUAGE 1033,
        TagsText    LANGUAGE 1033
    )
    KEY INDEX PK_Documents
    ON LeaseVaultCatalog
    WITH CHANGE_TRACKING AUTO, STOPLIST = SYSTEM;
GO

-- Sanity check: ranked prefix search, the same shape the application issues.
-- SELECT d.Id, d.Title, ft.[RANK]
-- FROM CONTAINSTABLE(dbo.Documents, (Title, Description, TagsText), '"lease*" AND "renewal*"') ft
-- JOIN dbo.Documents d ON d.Id = ft.[KEY]
-- ORDER BY ft.[RANK] DESC;
