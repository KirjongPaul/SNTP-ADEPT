-- SNTP-ADEPT tables in SNTP_PPO.
-- Same pattern as SNTP-Крипто Database/CryptoTables.sql:
--   SQL Server, dbo schema, product prefix (Adept*), IF OBJECT_ID IS NULL (no DROP).
-- App auto-creates these on startup when the login has CREATE TABLE.
-- If it does not, a DBA should run this script once.

IF OBJECT_ID(N'dbo.AdeptProjects', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptProjects (
        Id nvarchar(64) NOT NULL CONSTRAINT PK_AdeptProjects PRIMARY KEY,
        Name nvarchar(500) NOT NULL,
        PirJson nvarchar(max) NOT NULL,
        UpdatedAt datetimeoffset NOT NULL
    );

    CREATE INDEX IX_AdeptProjects_UpdatedAt ON dbo.AdeptProjects (UpdatedAt DESC);
END;

IF OBJECT_ID(N'dbo.AdeptCatalogKoeffs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptCatalogKoeffs (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptCatalogKoeffs PRIMARY KEY,
        SourceFile nvarchar(260) NOT NULL,
        GroupName nvarchar(500) NULL,
        ExternalId nvarchar(200) NULL,
        Name nvarchar(2000) NOT NULL,
        TypeName nvarchar(100) NULL,
        TypeNum int NULL,
        CombineType nvarchar(32) NULL,
        Value decimal(18,6) NULL,
        MinValue decimal(18,6) NULL,
        MaxValue decimal(18,6) NULL,
        HasRange bit NOT NULL CONSTRAINT DF_AdeptCatalogKoeffs_HasRange DEFAULT (0),
        Formula nvarchar(max) NULL,
        Status nvarchar(32) NULL,
        PromoType nvarchar(64) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
    CREATE INDEX IX_AdeptCatalogKoeffs_SourceFile ON dbo.AdeptCatalogKoeffs (SourceFile);
END;

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AdeptCatalogKoeffs_Name'
      AND object_id = OBJECT_ID(N'dbo.AdeptCatalogKoeffs'))
    DROP INDEX IX_AdeptCatalogKoeffs_Name ON dbo.AdeptCatalogKoeffs;

IF OBJECT_ID(N'dbo.AdeptCatalogKoeffs', N'U') IS NOT NULL
AND NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_AdeptCatalogKoeffs_SourceFile'
      AND object_id = OBJECT_ID(N'dbo.AdeptCatalogKoeffs'))
    CREATE INDEX IX_AdeptCatalogKoeffs_SourceFile ON dbo.AdeptCatalogKoeffs (SourceFile);

IF COL_LENGTH(N'dbo.AdeptCatalogKoeffs', N'ExternalId') IS NOT NULL
AND COL_LENGTH(N'dbo.AdeptCatalogKoeffs', N'ExternalId') < 400
    ALTER TABLE dbo.AdeptCatalogKoeffs ALTER COLUMN ExternalId nvarchar(200) NULL;

IF OBJECT_ID(N'dbo.AdeptCatalogIndexes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptCatalogIndexes (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptCatalogIndexes PRIMARY KEY,
        SourceFile nvarchar(260) NOT NULL,
        Name nvarchar(1000) NOT NULL,
        Value decimal(18,6) NULL,
        CurrentQuarter nvarchar(8) NULL,
        CurrentYear nvarchar(8) NULL,
        BaseYear nvarchar(8) NULL,
        TypeOfWork nvarchar(64) NULL,
        Office nvarchar(200) NULL,
        GroupName nvarchar(500) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
    CREATE INDEX IX_AdeptCatalogIndexes_Value ON dbo.AdeptCatalogIndexes (Value);
END;

IF OBJECT_ID(N'dbo.AdeptCatalogRates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptCatalogRates (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptCatalogRates PRIMARY KEY,
        SourceFile nvarchar(260) NOT NULL,
        Kind nvarchar(32) NOT NULL,
        Doc nvarchar(64) NULL,
        Year int NULL,
        TableCode nvarchar(64) NULL,
        Item nvarchar(64) NULL,
        Code nvarchar(128) NOT NULL,
        Name nvarchar(1000) NOT NULL,
        PriceA decimal(18,4) NULL,
        PriceB decimal(18,4) NULL,
        Unit nvarchar(64) NULL,
        Source nvarchar(32) NOT NULL,
        Raw nvarchar(max) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
    CREATE INDEX IX_AdeptCatalogRates_Code ON dbo.AdeptCatalogRates (Code);
END;

IF OBJECT_ID(N'dbo.AdeptCatalogRpds', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptCatalogRpds (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptCatalogRpds PRIMARY KEY,
        SourceFile nvarchar(260) NOT NULL,
        Folder nvarchar(500) NULL,
        GroupName nvarchar(500) NULL,
        Name nvarchar(1000) NOT NULL,
        ParentName nvarchar(1000) NULL,
        Share decimal(18,6) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
END;

-- 2026-10: рабочая папка, группы, ФГИС, справочник Газпром, съём «Базы ПИР».
IF COL_LENGTH(N'dbo.AdeptProjects', N'WorkFolder') IS NULL
    ALTER TABLE dbo.AdeptProjects ADD WorkFolder nvarchar(1000) NULL;

IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'GroupPath') IS NULL
    ALTER TABLE dbo.AdeptCatalogRates ADD GroupPath nvarchar(1000) NULL;
IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'ParentCode') IS NULL
    ALTER TABLE dbo.AdeptCatalogRates ADD ParentCode nvarchar(128) NULL;
IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'Multiplier') IS NULL
    ALTER TABLE dbo.AdeptCatalogRates ADD Multiplier nvarchar(64) NULL;

IF OBJECT_ID(N'dbo.AdeptCatalogVendor', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptCatalogVendor (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptCatalogVendor PRIMARY KEY,
        Vendor nvarchar(64) NOT NULL,
        Identifier nvarchar(64) NOT NULL,
        Caption nvarchar(500) NOT NULL,
        Value decimal(18,6) NULL,
        Role nvarchar(32) NULL,
        Stage nvarchar(32) NULL,
        Source nvarchar(32) NOT NULL,
        LetterRef nvarchar(500) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.AdeptFrsnPirSn', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptFrsnPirSn (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptFrsnPirSn PRIMARY KEY,
        PeriodId int NULL,
        Kind nvarchar(32) NOT NULL,
        Cipher nvarchar(200) NULL,
        Name nvarchar(1000) NOT NULL,
        OrderRef nvarchar(500) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.AdeptFrsnPirIndex', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdeptFrsnPirIndex (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptFrsnPirIndex PRIMARY KEY,
        PeriodId int NULL,
        Name nvarchar(500) NULL,
        Value decimal(18,6) NULL,
        Quarter nvarchar(8) NULL,
        Year nvarchar(8) NULL,
        BaseYear nvarchar(32) NULL,
        UpdatedAt datetimeoffset NOT NULL
    );
END;

