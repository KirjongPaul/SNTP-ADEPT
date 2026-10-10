using Dapper;
using Microsoft.Data.SqlClient;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Api;

public static class AdeptSchema
{
    public const string CreateTables = """
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

        IF COL_LENGTH(N'dbo.AdeptProjects', N'WorkFolder') IS NULL
            ALTER TABLE dbo.AdeptProjects ADD WorkFolder nvarchar(1000) NULL;

        IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'GroupPath') IS NULL
            ALTER TABLE dbo.AdeptCatalogRates ADD GroupPath nvarchar(1000) NULL;
        IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'ParentCode') IS NULL
            ALTER TABLE dbo.AdeptCatalogRates ADD ParentCode nvarchar(128) NULL;
        IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'Multiplier') IS NULL
            ALTER TABLE dbo.AdeptCatalogRates ADD Multiplier nvarchar(64) NULL;
        IF COL_LENGTH(N'dbo.AdeptCatalogRates', N'Code') IS NOT NULL
        AND COL_LENGTH(N'dbo.AdeptCatalogRates', N'Code') < 512
            ALTER TABLE dbo.AdeptCatalogRates ALTER COLUMN Code nvarchar(256) NOT NULL;

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

        IF NOT EXISTS (SELECT 1 FROM dbo.AdeptCatalogVendor WHERE Vendor = N'gazprom' AND Identifier = N'Kгазпром')
            INSERT INTO dbo.AdeptCatalogVendor (Vendor, Identifier, Caption, Value, Role, Source, UpdatedAt)
            VALUES (N'gazprom', N'Kгазпром', N'Коэффициент оптимизации ПАО «Газпром» (пилот)', 0.8, N'pricing', N'pilot-excel', SYSUTCDATETIME());
        IF NOT EXISTS (SELECT 1 FROM dbo.AdeptCatalogVendor WHERE Vendor = N'gazprom' AND Identifier = N'Kрайон')
            INSERT INTO dbo.AdeptCatalogVendor (Vendor, Identifier, Caption, Value, Role, Source, UpdatedAt)
            VALUES (N'gazprom', N'Kрайон', N'Районный коэффициент (пилот)', 1.08, N'district', N'pilot-excel', SYSUTCDATETIME());

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

        IF OBJECT_ID(N'dbo.AdeptBooks', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AdeptBooks (
                Id nvarchar(64) NOT NULL CONSTRAINT PK_AdeptBooks PRIMARY KEY,
                Kind nvarchar(16) NOT NULL,
                Title nvarchar(500) NOT NULL,
                OrderRef nvarchar(200) NULL,
                Status nvarchar(32) NOT NULL,
                Folder nvarchar(1000) NULL,
                BookJson nvarchar(max) NOT NULL,
                FileHash nvarchar(128) NULL,
                UpdatedAt datetimeoffset NOT NULL
            );
        END;

        IF OBJECT_ID(N'dbo.AdeptBookItems', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AdeptBookItems (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptBookItems PRIMARY KEY,
                BookId nvarchar(64) NOT NULL,
                TableCode nvarchar(32) NOT NULL,
                Item nvarchar(32) NOT NULL,
                Name nvarchar(1000) NOT NULL,
                ParamUnit nvarchar(64) NULL,
                Xmin decimal(18,4) NULL,
                Xmax decimal(18,4) NULL,
                PriceA decimal(18,4) NULL,
                PriceB decimal(18,4) NULL,
                CheckStatus nvarchar(32) NULL,
                Page int NULL
            );
            CREATE INDEX IX_AdeptBookItems_Book ON dbo.AdeptBookItems (BookId, TableCode, Item);
        END;

        IF OBJECT_ID(N'dbo.AdeptBookNotes', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AdeptBookNotes (
                Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdeptBookNotes PRIMARY KEY,
                BookId nvarchar(64) NOT NULL,
                Anchor nvarchar(80) NOT NULL,
                LetterRef nvarchar(300) NOT NULL,
                Quote nvarchar(max) NOT NULL,
                Page int NULL,
                FileName nvarchar(260) NULL
            );
            CREATE INDEX IX_AdeptBookNotes_Anchor ON dbo.AdeptBookNotes (BookId, Anchor);
        END;

        IF COL_LENGTH(N'dbo.AdeptBookNotes', N'Quote') IS NOT NULL
        AND COL_LENGTH(N'dbo.AdeptBookNotes', N'Quote') <> -1
            ALTER TABLE dbo.AdeptBookNotes ALTER COLUMN Quote nvarchar(max) NOT NULL;
        """;
}

public sealed partial class CatalogStore(IConfiguration configuration, ILogger<CatalogStore> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(AdeptSchema.CreateTables, cancellationToken: cancellationToken));
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.AdeptCatalogKoeffs", cancellationToken: cancellationToken));
        if (count == 0)
        {
            var dir = configuration["AdeptCatalog:AprDirectory"] ?? @"C:\Adept\AUS-client\catalogs";
            if (Directory.Exists(dir))
            {
                logger.LogInformation("Catalog empty — syncing Adept .apr from {Dir} (without type_rpds/type_sostav)", dir);
                await SyncAprAsync(dir, includeHeavy: false, cancellationToken);
            }
        }
    }

    public async Task<CatalogSyncResult> SyncAprAsync(string? directory = null, bool includeHeavy = true, CancellationToken cancellationToken = default)
    {
        var dir = directory ?? configuration["AdeptCatalog:AprDirectory"] ?? @"C:\Adept\AUS-client\catalogs";
        if (!Directory.Exists(dir))
            return new CatalogSyncResult(false, dir, 0, 0, 0, 0, $"Нет каталога {dir}");

        IEnumerable<string>? files = includeHeavy
            ? null
            : AprXml.DefaultImportFiles.Where(n =>
                !n.StartsWith("type_rpds", StringComparison.OrdinalIgnoreCase)
                && !n.StartsWith("type_sostav", StringComparison.OrdinalIgnoreCase));
        var parsed = AprXml.LoadDirectory(dir, files);
        var now = DateTimeOffset.UtcNow;
        await using var connection = CreateConnection();
        connection.Open();
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);

        var koeffs = 0;
        var indexes = 0;
        var rates = 0;
        var rpds = 0;
        foreach (var file in parsed)
        {
            if (file.Error is not null)
            {
                logger.LogWarning("APR {File}: {Error}", file.FileName, file.Error);
                continue;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.AdeptCatalogKoeffs WHERE SourceFile=@f; DELETE FROM dbo.AdeptCatalogIndexes WHERE SourceFile=@f; DELETE FROM dbo.AdeptCatalogRates WHERE SourceFile=@f AND Source<>N'excel-seed'; DELETE FROM dbo.AdeptCatalogRpds WHERE SourceFile=@f;",
                new { f = file.FileName }, transaction: tx, cancellationToken: cancellationToken));

            foreach (var chunk in file.Koeffs.Chunk(200))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptCatalogKoeffs
                    (SourceFile, GroupName, ExternalId, Name, TypeName, TypeNum, CombineType, Value, MinValue, MaxValue, HasRange, Formula, Status, PromoType, UpdatedAt)
                    VALUES (@SourceFile, @GroupName, @ExternalId, @Name, @TypeName, @TypeNum, @CombineType, @Value, @MinValue, @MaxValue, @HasRange, @Formula, @Status, @PromoType, @UpdatedAt)
                    """,
                    chunk.Select(k => new
                    {
                        k.SourceFile, k.GroupName, k.ExternalId, k.Name, k.TypeName, k.TypeNum, k.CombineType,
                        k.Value, k.MinValue, k.MaxValue, k.HasRange, k.Formula, k.Status, k.PromoType,
                        UpdatedAt = now
                    }), transaction: tx, cancellationToken: cancellationToken));
                koeffs += chunk.Length;
            }

            foreach (var chunk in file.Indexes.Chunk(200))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptCatalogIndexes
                    (SourceFile, Name, Value, CurrentQuarter, CurrentYear, BaseYear, TypeOfWork, Office, GroupName, UpdatedAt)
                    VALUES (@SourceFile, @Name, @Value, @CurrentQuarter, @CurrentYear, @BaseYear, @TypeOfWork, @Office, @GroupName, @UpdatedAt)
                    """,
                    chunk.Select(i => new
                    {
                        i.SourceFile, i.Name, i.Value, i.CurrentQuarter, i.CurrentYear, i.BaseYear, i.TypeOfWork, i.Office, i.GroupName,
                        UpdatedAt = now
                    }), transaction: tx, cancellationToken: cancellationToken));
                indexes += chunk.Length;
            }

            foreach (var chunk in file.Rates.Chunk(200))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptCatalogRates
                    (SourceFile, Kind, Doc, Year, TableCode, Item, Code, Name, PriceA, PriceB, Unit, Source, Raw, UpdatedAt)
                    VALUES (@SourceFile, @Kind, @Doc, @Year, @Table, @Item, @Code, @Name, @PriceA, @PriceB, @Unit, @Source, @Raw, @UpdatedAt)
                    """,
                    chunk.Select(r => new
                    {
                        r.SourceFile, r.Kind, r.Doc, r.Year, Table = r.Table, r.Item, r.Code, r.Name, r.PriceA, r.PriceB, r.Unit, r.Source, r.Raw,
                        UpdatedAt = now
                    }), transaction: tx, cancellationToken: cancellationToken));
                rates += chunk.Length;
            }

            foreach (var chunk in file.Rpds.Chunk(200))
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptCatalogRpds
                    (SourceFile, Folder, GroupName, Name, ParentName, Share, UpdatedAt)
                    VALUES (@SourceFile, @Folder, @GroupName, @Name, @ParentName, @Share, @UpdatedAt)
                    """,
                    chunk.Select(r => new
                    {
                        r.SourceFile, r.Folder, r.GroupName, r.Name, r.ParentName, r.Share,
                        UpdatedAt = now
                    }), transaction: tx, cancellationToken: cancellationToken));
                rpds += chunk.Length;
            }
        }

        await tx.CommitAsync(cancellationToken);
        return new CatalogSyncResult(true, dir, koeffs, indexes, rates, rpds, null);
    }

    public async Task SeedRatesFromDocumentAsync(PirDocument doc, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<object>();
        foreach (var est in doc.Estimates)
        foreach (var pos in est.Positions.Where(p => p.Kind == PositionKind.Work && p.Basis is not null))
        {
            var rate = AprXml.FromBasis(pos.Basis!, pos.Name, pos.PriceA, pos.PriceB, doc.Source.File ?? "excel");
            if (rate is null || string.IsNullOrWhiteSpace(rate.Code) || rate.Code.Length < 5) continue;
            rows.Add(new
            {
                SourceFile = rate.SourceFile,
                rate.Kind,
                rate.Doc,
                rate.Year,
                Table = rate.Table,
                rate.Item,
                rate.Code,
                rate.Name,
                rate.PriceA,
                rate.PriceB,
                rate.Unit,
                rate.Source,
                rate.Raw,
                UpdatedAt = now
            });
        }

        if (rows.Count == 0) return;
        await using var connection = CreateConnection();
        foreach (var row in rows)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                IF NOT EXISTS (SELECT 1 FROM dbo.AdeptCatalogRates WHERE Code=@Code AND Source=N'excel-seed')
                INSERT INTO dbo.AdeptCatalogRates
                (SourceFile, Kind, Doc, Year, TableCode, Item, Code, Name, PriceA, PriceB, Unit, Source, Raw, UpdatedAt)
                VALUES (@SourceFile, @Kind, @Doc, @Year, @Table, @Item, @Code, @Name, @PriceA, @PriceB, @Unit, @Source, @Raw, @UpdatedAt);
                """,
                row, cancellationToken: cancellationToken));
        }
    }

    public async Task AnnotateAsync(PirDocument doc, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var rates = (await connection.QueryAsync<CatalogCandidate>(new CommandDefinition(
            """
            SELECT Code, Name, Doc, TableCode AS [Table], Item, GroupPath, Kind, PriceA, PriceB, Source, Raw
            FROM dbo.AdeptCatalogRates
            """,
            cancellationToken: cancellationToken))).AsList();
        foreach (var est in doc.Estimates)
        foreach (var pos in est.Positions)
        {
            if (!string.IsNullOrWhiteSpace(pos.CatalogCode) || pos.Basis is not null)
            {
                var text = string.Join("\n", new[] { pos.Basis?.Raw, pos.CatalogCode, pos.Code, pos.Name }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var found = CatalogMatcher.Search(text, rates);
                if (pos.Basis is not null) pos.Basis.MatchStatus = found.Status;
                if (found.Status == "matched" && found.Hits.Count == 1)
                    pos.CatalogCode = found.Hits[0].Code;
                else if (found.Status == "ambiguous")
                {
                    doc.Issues.Add(new Clarification
                    {
                        Severity = ClarificationSeverity.Warning,
                        Code = "ambiguous",
                        Message = "Несколько расценок подходят к строке: " + string.Join("; ", found.Hits.Take(4).Select(h => h.Code)),
                        EstimateId = est.Id,
                        PositionId = pos.Id,
                        Field = "basis"
                    });
                }
                else if (found.Status == "catalog-miss" && pos.Basis is { Kind: not "" and not "Unknown" })
                {
                    doc.Issues.Add(new Clarification
                    {
                        Severity = ClarificationSeverity.Info,
                        Code = "catalog-miss",
                        Message = $"Расценка «{pos.Basis.Raw}» не найдена в каталоге. Для СН без A/B это норма.",
                        EstimateId = est.Id,
                        PositionId = pos.Id,
                        Field = "basis"
                    });
                }
            }

            foreach (var f in pos.Factors.Where(x => x.Role == "index"))
            {
                var idx = await connection.QueryFirstOrDefaultAsync<IndexHit>(new CommandDefinition(
                    """
                    SELECT TOP 1 Name, Value FROM dbo.AdeptCatalogIndexes
                    WHERE (@Caption IS NOT NULL AND Name LIKE N'%' + @Caption + N'%')
                       OR (Value IS NOT NULL AND @Value IS NOT NULL AND ABS(Value - @Value) < 0.0005)
                    ORDER BY CASE WHEN Name LIKE N'%пр.раб%' THEN 0 ELSE 1 END
                    """,
                    new { Caption = f.Caption is "Ктек" or null ? null : f.Caption, Value = f.WorkDoc ?? f.ProjectDoc },
                    cancellationToken: cancellationToken));
                if (idx is not null)
                {
                    f.CatalogKey = idx.Name;
                    f.Source = f.Source is FactorSource.Formula or FactorSource.Text or FactorSource.Both
                        ? f.Source
                        : FactorSource.Index;
                }
                else
                {
                    var value = f.WorkDoc ?? f.ProjectDoc;
                    var letter156 = value is 1.56m;
                    doc.Issues.Add(new Clarification
                    {
                        Severity = ClarificationSeverity.Info,
                        Code = "catalog-miss",
                        Message = letter156
                            ? "Индекс 1,56 (письмо 45358-ИФ/09, III кв. 2026) отсутствует в снимке .apr и во ФГИС ЦС (там ещё 2 кв., 1,53). Это лаг реестра, не ошибка строки."
                            : $"Индекс {f.Identifier}={value} не найден в dbo.AdeptCatalogIndexes (снимок .apr может быть старше письма Минстроя).",
                        EstimateId = est.Id,
                        PositionId = pos.Id,
                        Field = "factors"
                    });
                }
            }
        }
    }

    public async Task<IReadOnlyList<CatalogKoeff>> SearchKoeffsAsync(string? q, int take = 40, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var like = string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim() + "%";
        var rows = await connection.QueryAsync<CatalogKoeff>(new CommandDefinition(
            """
            SELECT TOP (@Take) SourceFile, GroupName, ExternalId, Name, TypeName, TypeNum, CombineType,
                   Value, MinValue, MaxValue, HasRange, Formula, Status, PromoType
            FROM dbo.AdeptCatalogKoeffs
            WHERE @Like IS NULL OR Name LIKE @Like OR GroupName LIKE @Like
            ORDER BY Name
            """,
            new { Take = take, Like = like },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CatalogIndex>> SearchIndexesAsync(string? q, int take = 40, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var like = string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim() + "%";
        var rows = await connection.QueryAsync<CatalogIndex>(new CommandDefinition(
            """
            SELECT TOP (@Take) SourceFile, Name, Value, CurrentQuarter, CurrentYear, BaseYear, TypeOfWork, Office, GroupName
            FROM dbo.AdeptCatalogIndexes
            WHERE @Like IS NULL OR Name LIKE @Like
            ORDER BY CurrentYear DESC, CurrentQuarter DESC, Name
            """,
            new { Take = take, Like = like },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CatalogRate>> SearchRatesAsync(string? q, int take = 40, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var like = string.IsNullOrWhiteSpace(q) ? null : "%" + q.Trim() + "%";
        var rows = await connection.QueryAsync<CatalogRate>(new CommandDefinition(
            """
            SELECT TOP (@Take) SourceFile, Kind, Doc, Year, TableCode AS [Table], Item, Code, Name, PriceA, PriceB, Unit, Source, Raw
            FROM dbo.AdeptCatalogRates
            WHERE @Like IS NULL OR Code LIKE @Like OR Name LIKE @Like OR Doc LIKE @Like OR Raw LIKE @Like
            ORDER BY Code
            """,
            new { Take = take, Like = like },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<CatalogStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var k = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.AdeptCatalogKoeffs", cancellationToken: cancellationToken));
        var i = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.AdeptCatalogIndexes", cancellationToken: cancellationToken));
        var r = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.AdeptCatalogRates", cancellationToken: cancellationToken));
        var p = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.AdeptCatalogRpds", cancellationToken: cancellationToken));
        var sn = await ScalarOrZero(connection, "SELECT COUNT(*) FROM dbo.AdeptFrsnPirSn", cancellationToken);
        var fi = await ScalarOrZero(connection, "SELECT COUNT(*) FROM dbo.AdeptFrsnPirIndex", cancellationToken);
        var v = await ScalarOrZero(connection, "SELECT COUNT(*) FROM dbo.AdeptCatalogVendor", cancellationToken);
        return new CatalogStatus(k, i, r, p) { FrsnSn = sn, FrsnIndexes = fi, Vendor = v };
    }

    private static async Task<int> ScalarOrZero(Microsoft.Data.SqlClient.SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        try
        {
            return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        }
        catch
        {
            return 0;
        }
    }

    private SqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("SNTP_PPO");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string SNTP_PPO is not configured.");
        return new SqlConnection(connectionString);
    }

    private sealed class RateHit
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class IndexHit
    {
        public string Name { get; set; } = "";
        public decimal? Value { get; set; }
    }
}

public sealed record CatalogSyncResult(bool Ok, string Directory, int Koeffs, int Indexes, int Rates, int Rpds, string? Error);
public sealed record CatalogStatus(int Koeffs, int Indexes, int Rates, int Rpds)
{
    public int FrsnSn { get; init; }
    public int FrsnIndexes { get; init; }
    public int Vendor { get; init; }
}
