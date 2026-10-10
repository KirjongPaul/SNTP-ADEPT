using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Sntp.Adept.Domain.Catalog;

namespace Sntp.Adept.Api;

public sealed class FgisCsClient(HttpClient http)
{
    public const string Base = "https://fgiscs.minstroyrf.ru";

    public async Task<FgisSyncResult> SyncAsync(Func<string, int?, string, IReadOnlyList<FgisSnRow>, IReadOnlyList<FgisIndexRow>, CancellationToken, Task> save, CancellationToken cancellationToken)
    {
        try
        {
            var periodsJson = await GetAsync("/api/PirIndex/Periods", cancellationToken);
            var periods = FgisCsParser.ParsePeriods(periodsJson);
            var active = periods.FirstOrDefault(p => p.Active)?.Id ?? periods.LastOrDefault()?.Id;
            if (active is null)
                return new FgisSyncResult(false, 0, 0, 0, "ФГИС ЦС не вернул периоды");

            var designJson = await GetAsync($"/api/SurveyDesignSn?periodId={active}", cancellationToken);
            var surveyJson = await GetAsync($"/api/SurveyWorkSn?periodId={active}", cancellationToken);
            var indexJson = await GetAsync($"/api/PirIndex?periodId={active}", cancellationToken);
            var design = FgisCsParser.ParseSn(designJson, "design", active);
            var survey = FgisCsParser.ParseSn(surveyJson, "survey", active);
            var indexes = FgisCsParser.ParseIndexes(indexJson, active);
            var sn = design.Concat(survey).ToList();
            await save(periodsJson, active, indexJson, sn, indexes, cancellationToken);
            return new FgisSyncResult(true, sn.Count, indexes.Count, active.Value, null);
        }
        catch (Exception ex)
        {
            return new FgisSyncResult(false, 0, 0, 0, ex.Message);
        }
    }

    private async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(Base + path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}

public sealed record FgisSyncResult(bool Ok, int Sn, int Indexes, int PeriodId, string? Error);

public sealed partial class CatalogStore
{
    public async Task<IReadOnlyList<VendorCoeff>> VendorAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var rows = await connection.QueryAsync<VendorCoeff>(new CommandDefinition(
            "SELECT Identifier, Caption, Value, Role, Source, LetterRef FROM dbo.AdeptCatalogVendor WHERE Vendor=N'gazprom' ORDER BY Identifier",
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<CatalogGroups> GroupsAsync(string? kind, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        kind = (kind ?? "rates").ToLowerInvariant();
        var nodes = kind switch
        {
            "koeffs" => await Groups(connection, "SELECT GroupName AS [Key], COUNT(*) AS Cnt FROM dbo.AdeptCatalogKoeffs GROUP BY GroupName ORDER BY GroupName", cancellationToken),
            "indexes" => await Groups(connection, """
                SELECT CONCAT(ISNULL(CurrentYear,N'?'), N' / ', ISNULL(CurrentQuarter,N'?'), N' / ', ISNULL(Office,N'')) AS [Key], COUNT(*) AS Cnt
                FROM dbo.AdeptCatalogIndexes GROUP BY CurrentYear, CurrentQuarter, Office
                ORDER BY CurrentYear DESC, CurrentQuarter DESC
                """, cancellationToken),
            "rpds" => await Groups(connection, """
                SELECT CONCAT(ISNULL(Folder,N''), N' / ', ISNULL(GroupName,N'')) AS [Key], COUNT(*) AS Cnt
                FROM dbo.AdeptCatalogRpds GROUP BY Folder, GroupName ORDER BY Folder, GroupName
                """, cancellationToken),
            "gazprom" => await Groups(connection, "SELECT Identifier AS [Key], COUNT(*) AS Cnt FROM dbo.AdeptCatalogVendor WHERE Vendor=N'gazprom' GROUP BY Identifier", cancellationToken),
            _ => await Groups(connection, """
                SELECT CONCAT(ISNULL(Kind,N''), N' / ', ISNULL(Doc,N'без сборника'), N' / ', ISNULL(TableCode,N'')) AS [Key], COUNT(*) AS Cnt
                FROM dbo.AdeptCatalogRates GROUP BY Kind, Doc, TableCode ORDER BY Kind, Doc, TableCode
                """, cancellationToken)
        };
        return new CatalogGroups(kind, nodes);
    }

    public async Task<LineSearchResult> SearchLineAsync(string? text, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var rates = (await connection.QueryAsync<CatalogCandidate>(new CommandDefinition(
            "SELECT Code, Name, Doc, TableCode AS [Table], Item, GroupPath, Kind, PriceA, PriceB, Source, Raw FROM dbo.AdeptCatalogRates",
            cancellationToken: cancellationToken))).AsList();
        var vendor = (await connection.QueryAsync<VendorCoeff>(new CommandDefinition(
            "SELECT Identifier, Caption, Value, Role, Source, LetterRef FROM dbo.AdeptCatalogVendor WHERE Vendor=N'gazprom'",
            cancellationToken: cancellationToken))).AsList();
        try
        {
            var frsn = await connection.QueryAsync<CatalogCandidate>(new CommandDefinition(
                "SELECT ISNULL(Cipher, Name) AS Code, Name, Cipher AS Doc, Kind, NULL AS PriceA, NULL AS PriceB, N'fgis' AS Source FROM dbo.AdeptFrsnPirSn",
                cancellationToken: cancellationToken));
            rates.AddRange(frsn);
        }
        catch
        {
            /* таблица появится после инициализации */
        }
        return CatalogMatcher.Search(text, rates, vendor);
    }

    public async Task<int> ImportGazpromAsync(IReadOnlyList<VendorCoeff> rows, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var n = 0;
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Identifier)) continue;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.AdeptCatalogVendor WHERE Vendor=N'gazprom' AND Identifier=@Identifier AND Source=N'user-import';
                INSERT INTO dbo.AdeptCatalogVendor (Vendor, Identifier, Caption, Value, Role, Source, LetterRef, UpdatedAt)
                VALUES (N'gazprom', @Identifier, @Caption, @Value, @Role, N'user-import', @LetterRef, SYSUTCDATETIME());
                """,
                row, cancellationToken: cancellationToken));
            n++;
        }
        return n;
    }

    public async Task<FgisSyncResult> SyncFgisAsync(FgisCsClient client, CancellationToken cancellationToken = default)
    {
        return await client.SyncAsync(async (_, period, _, sn, indexes, ct) =>
        {
            await using var connection = CreateConnection();
            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM dbo.AdeptFrsnPirSn WHERE PeriodId=@Period; DELETE FROM dbo.AdeptFrsnPirIndex WHERE PeriodId=@Period;",
                new { Period = period }, cancellationToken: ct));
            foreach (var row in sn)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptFrsnPirSn (PeriodId, Kind, Cipher, Name, OrderRef, UpdatedAt)
                    VALUES (@PeriodId, @Kind, @Cipher, @Name, @OrderRef, SYSUTCDATETIME())
                    """,
                    row, cancellationToken: ct));
            }
            foreach (var row in indexes)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptFrsnPirIndex (PeriodId, Name, Value, Quarter, Year, BaseYear, UpdatedAt)
                    VALUES (@PeriodId, @Name, @Value, @Quarter, @Year, @BaseYear, SYSUTCDATETIME())
                    """,
                    row, cancellationToken: ct));
            }
        }, cancellationToken);
    }

    public async Task<int> SaveHarvestAsync(IReadOnlyList<AdeptUiRate.HarvestRow> rows, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var n = 0;
        foreach (var row in rows)
        {
            if (row.Qualifier) continue;
            var basis = row.Basis;
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.AdeptCatalogRates WHERE Code=@Code AND Source=N'adept-ui';
                INSERT INTO dbo.AdeptCatalogRates
                (SourceFile, Kind, Doc, Year, TableCode, Item, Code, Name, PriceA, PriceB, Unit, Source, Raw, GroupPath, ParentCode, Multiplier, UpdatedAt)
                VALUES (N'adept-ui', @Kind, @Doc, @Year, @Table, @Item, @Code, @Name, @PriceA, @PriceB, @Unit, N'adept-ui', @Raw, @GroupPath, @ParentCode, @Multiplier, SYSUTCDATETIME());
                """,
                new
                {
                    Kind = string.IsNullOrWhiteSpace(basis.Kind) ? "SBC" : basis.Kind,
                    basis.Doc,
                    basis.Year,
                    Table = basis.Table,
                    basis.Item,
                    Code = string.IsNullOrWhiteSpace(row.Code) ? AprXml.RateCode(basis) : row.Code,
                    Name = string.IsNullOrWhiteSpace(row.Name) ? row.Code : row.Name,
                    row.PriceA,
                    row.PriceB,
                    Unit = row.Unit,
                    Raw = row.Code,
                    row.GroupPath,
                    row.ParentCode,
                    Multiplier = row.Multiplier
                },
                cancellationToken: cancellationToken));
            n++;
        }
        return n;
    }

    private static async Task<List<CatalogGroupNode>> Groups(Microsoft.Data.SqlClient.SqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<GroupRow>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return rows.Select(r => new CatalogGroupNode(r.Key ?? "(без группы)", r.Cnt)).ToList();
    }

    private sealed class GroupRow
    {
        public string? Key { get; set; }
        public int Cnt { get; set; }
    }
}

public sealed record CatalogGroupNode(string Name, int Count);
public sealed record CatalogGroups(string Kind, IReadOnlyList<CatalogGroupNode> Nodes);
