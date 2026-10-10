using Dapper;
using Microsoft.Data.SqlClient;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Json;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Api;

public sealed class ProjectStore(IConfiguration configuration, ILogger<ProjectStore> logger)
{
    public string WorkRoot => WorkCatalog.Ensure(configuration["Adept:WorkRoot"]);

    public void EnsureFolder(PirDocument doc)
    {
        if (!string.IsNullOrWhiteSpace(doc.Project.WorkFolder) && File.Exists(Path.Combine(doc.Project.WorkFolder, "project.json")))
            return;
        WorkFolder.Create(doc, WorkRoot);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            AdeptSchema.CreateTables,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<PirDocument> SaveAsync(PirDocument doc, CancellationToken cancellationToken = default)
    {
        await WriteSqlAsync(doc, cancellationToken);
        if (!string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
        {
            Directory.CreateDirectory(doc.Project.WorkFolder);
            WorkFolder.Sync(doc);
        }
        return doc;
    }

    /// <summary>Копия в SQL без записи в папку объекта.</summary>
    public async Task WriteSqlAsync(PirDocument doc, CancellationToken cancellationToken = default)
    {
        var json = PirJson.Serialize(doc);
        await using var connection = CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            MERGE dbo.AdeptProjects AS target
            USING (SELECT @Id AS Id) AS source
            ON target.Id = source.Id
            WHEN MATCHED THEN
                UPDATE SET Name = @Name, PirJson = @PirJson, WorkFolder = @WorkFolder, UpdatedAt = @UpdatedAt
            WHEN NOT MATCHED THEN
                INSERT (Id, Name, PirJson, WorkFolder, UpdatedAt)
                VALUES (@Id, @Name, @PirJson, @WorkFolder, @UpdatedAt);
            """,
            new
            {
                Id = doc.Id,
                Name = doc.Project.ObjectName,
                PirJson = json,
                WorkFolder = doc.Project.WorkFolder,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Шапка из project.json и сметы из estimates поверх копии в SQL.
    /// Свод, уточнения и настройки, которых нет в project.json, остаются из SQL.
    /// </summary>
    public async Task<PirDocument?> ReadFolderAsync(string id, CancellationToken cancellationToken = default)
    {
        var doc = await GetAsync(id, cancellationToken);
        if (doc is null) return null;
        var folder = doc.Project.WorkFolder;
        if (string.IsNullOrWhiteSpace(folder) || !File.Exists(Path.Combine(folder, "project.json")))
            throw new InvalidOperationException("У объекта нет папки с project.json");
        var disk = WorkFolder.Load(folder);
        doc.Project.ObjectName = disk.Project.ObjectName;
        doc.Project.Stage = disk.Project.Stage;
        doc.Project.StageLabel = disk.Project.StageLabel;
        doc.Project.Customer = disk.Project.Customer;
        doc.Project.Designer = disk.Project.Designer;
        doc.Project.VatRate = disk.Project.VatRate;
        doc.Project.CurrentIndex = disk.Project.CurrentIndex;
        doc.Estimates = disk.Estimates;
        if (disk.Survey is not null) doc.Survey = disk.Survey;
        return doc;
    }

    public async Task<PirDocument?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ProjectRow? row = null;
        try
        {
            await using var connection = CreateConnection();
            row = await connection.QuerySingleOrDefaultAsync<ProjectRow>(new CommandDefinition(
                "SELECT PirJson, WorkFolder FROM dbo.AdeptProjects WHERE Id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQL недоступен, объект ищется в каталоге проектов");
        }

        if (row is null)
        {
            var folder = WorkCatalog.FindFolder(WorkRoot, id);
            return folder is null ? null : WorkFolder.Load(folder);
        }
        var doc = PirJson.Deserialize(row.PirJson);
        if (!string.IsNullOrWhiteSpace(row.WorkFolder) && File.Exists(Path.Combine(row.WorkFolder, "project.json")))
        {
            doc.Project.WorkFolder = row.WorkFolder;
            WorkFolder.Rescan(doc);
            return doc;
        }
        var fromRoot = WorkCatalog.FindFolder(WorkRoot, id);
        if (fromRoot is not null)
            return WorkFolder.Load(fromRoot);
        return doc;
    }

    public async Task<IReadOnlyList<ProjectListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var disk = WorkCatalog.Scan(WorkRoot)
            .Select(p => new ProjectListItem(p.Id, p.Name, p.Updated.ToString("o"), p.Stage, p.EstimateCount, p.Folder, p.Designer, p.Authors, p.Total, p.Estimates))
            .ToList();
        return disk;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        var n = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM dbo.AdeptProjects WHERE Id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return n > 0;
    }

    private SqlConnection CreateConnection()
    {
        var connectionString = configuration.GetConnectionString("SNTP_PPO");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("SNTP_PPO connection string is not configured.");
            throw new InvalidOperationException("Connection string SNTP_PPO is not configured.");
        }

        return new SqlConnection(connectionString);
    }

    private sealed class AdeptProjectListRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class ProjectRow
    {
        public string PirJson { get; set; } = "";
        public string? WorkFolder { get; set; }
    }
}

public sealed record ProjectListItem(
    string Id,
    string Name,
    string UpdatedAt,
    string Stage,
    int EstimateCount,
    string Folder,
    string Designer,
    IReadOnlyList<string> Authors,
    decimal? Total,
    IReadOnlyList<CatalogEstimate> Estimates);
