using System.Text.Json;
using Sntp.Adept.Domain.Json;

namespace Sntp.Adept.Domain.Documents;

public static class WorkCatalog
{
    public const string DefaultRoot = @"D:\NET\=TEST-ADEPT";

    public static string Ensure(string? configured)
    {
        var root = string.IsNullOrWhiteSpace(configured) ? DefaultRoot : configured.Trim();
        Directory.CreateDirectory(root);
        return root;
    }

    public static IReadOnlyList<CatalogProject> Scan(string root)
    {
        if (!Directory.Exists(root)) return [];
        var list = new List<CatalogProject>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var project = Path.Combine(dir, "project.json");
            if (!File.Exists(project)) continue;
            try
            {
                var file = JsonSerializer.Deserialize<WorkFolder.ProjectFile>(File.ReadAllText(project), PirJson.Options);
                if (file is null || string.IsNullOrWhiteSpace(file.Id)) continue;
                var updated = new DateTimeOffset(File.GetLastWriteTimeUtc(project), TimeSpan.Zero);
                var estimatesDir = Path.Combine(dir, "estimates");
                var estimateFiles = Directory.Exists(estimatesDir) ? Directory.GetFiles(estimatesDir, "*.json") : [];
                var heads = ReadHeads(estimateFiles);
                list.Add(new CatalogProject(
                    file.Id,
                    string.IsNullOrWhiteSpace(file.ObjectName) ? Path.GetFileName(dir) : file.ObjectName,
                    dir,
                    updated,
                    ShortStage(file.Stage),
                    estimateFiles.Length,
                    (file.Designer ?? "").Trim(),
                    heads.Authors,
                    heads.Total,
                    heads.Estimates));
            }
            catch
            {
                /* битый project.json не попадает в список */
            }
        }
        return list.OrderByDescending(i => i.Updated).ToList();
    }

    public static string? FindFolder(string root, string id)
    {
        if (!Directory.Exists(root)) return null;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var project = Path.Combine(dir, "project.json");
            if (!File.Exists(project)) continue;
            try
            {
                var file = JsonSerializer.Deserialize<WorkFolder.ProjectFile>(File.ReadAllText(project), PirJson.Options);
                if (file is not null && string.Equals(file.Id, id, StringComparison.OrdinalIgnoreCase))
                    return dir;
            }
            catch
            {
                /* skip */
            }
        }
        return null;
    }

    private static string ShortStage(string? stage) => stage switch
    {
        "WorkDoc" => "РД",
        "WorkProject" => "РП",
        _ => "П"
    };

    private static (IReadOnlyList<string> Authors, decimal? Total, IReadOnlyList<CatalogEstimate> Estimates) ReadHeads(string[] estimateFiles)
    {
        var estimates = new List<CatalogEstimate>();
        foreach (var path in estimateFiles)
        {
            try
            {
                var body = JsonSerializer.Deserialize<HeadFile>(File.ReadAllText(path), PirJson.Options)?.Estimate;
                if (body is null) continue;
                estimates.Add(new CatalogEstimate(
                    body.Id ?? "",
                    (body.Number ?? "").Trim(),
                    string.IsNullOrWhiteSpace(body.Title) ? (body.Number ?? "").Trim() : body.Title.Trim(),
                    body.OutlineDepth,
                    body.EngineAmount ?? body.ExcelAmount,
                    body.Author?.Trim() ?? "",
                    body.Form ?? ""));
            }
            catch
            {
                /* битый файл сметы не мешает списку */
            }
        }

        estimates.Sort(CompareOutline);
        var authors = new List<string>();
        foreach (var est in estimates)
        {
            if (est.Author.Length > 0 && !authors.Contains(est.Author, StringComparer.OrdinalIgnoreCase))
                authors.Add(est.Author);
        }

        decimal? total = estimates.Any(e => e.Amount is not null)
            ? estimates.Sum(e => e.Amount ?? 0)
            : null;
        return (authors, total, estimates);
    }

    private static int CompareOutline(CatalogEstimate a, CatalogEstimate b)
    {
        var ap = Parts(a.Number);
        var bp = Parts(b.Number);
        if (ap.Count == 0 && bp.Count == 0) return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        if (ap.Count == 0) return 1;
        if (bp.Count == 0) return -1;
        var n = Math.Max(ap.Count, bp.Count);
        for (var i = 0; i < n; i++)
        {
            if (i >= ap.Count) return -1;
            if (i >= bp.Count) return 1;
            var c = ap[i].CompareTo(bp[i]);
            if (c != 0) return c;
        }
        return 0;
    }

    private static List<int> Parts(string number)
    {
        var parts = new List<int>();
        if (string.IsNullOrWhiteSpace(number)) return parts;
        foreach (var piece in number.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            parts.Add(int.TryParse(piece, out var n) ? n : int.MaxValue);
        return parts;
    }

    private sealed class HeadFile
    {
        public HeadEstimate? Estimate { get; set; }
    }

    private sealed class HeadEstimate
    {
        public string? Id { get; set; }
        public string? Number { get; set; }
        public string? Title { get; set; }
        public int OutlineDepth { get; set; }
        public decimal? EngineAmount { get; set; }
        public decimal? ExcelAmount { get; set; }
        public string? Author { get; set; }
        public string? Form { get; set; }
    }
}

public sealed record CatalogEstimate(string Id, string Number, string Title, int Depth, decimal? Amount, string Author, string Form);

public sealed record CatalogProject(
    string Id,
    string Name,
    string Folder,
    DateTimeOffset Updated,
    string Stage,
    int EstimateCount,
    string Designer,
    IReadOnlyList<string> Authors,
    decimal? Total,
    IReadOnlyList<CatalogEstimate> Estimates);
