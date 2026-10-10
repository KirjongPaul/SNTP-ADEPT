using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Json;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

public static class WorkFolder
{
    public static string Create(PirDocument doc, string root)
    {
        var name = Sanitize(string.IsNullOrWhiteSpace(doc.Project.ObjectName) ? "object" : doc.Project.ObjectName);
        var dir = Path.Combine(root, name);
        if (Directory.Exists(dir))
            dir = Path.Combine(root, name + "-" + doc.Id[..6]);
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "estimates"));
        Directory.CreateDirectory(Path.Combine(dir, "assignments"));
        Directory.CreateDirectory(Path.Combine(dir, "scope"));
        Directory.CreateDirectory(Path.Combine(dir, "vor"));
        Directory.CreateDirectory(Path.Combine(dir, "sources"));
        doc.Project.WorkFolder = dir;
        WriteProject(doc);
        WriteDesktopIni(doc);
        foreach (var est in doc.Estimates)
            WriteEstimate(doc, est);
        SourceCatalog.Ensure(doc);
        return dir;
    }

    public static void WriteProject(PirDocument doc)
    {
        var dir = Require(doc);
        var payload = new ProjectFile
        {
            Id = doc.Id,
            ObjectName = doc.Project.ObjectName,
            Stage = doc.Project.Stage.ToString(),
            StageLabel = doc.Project.StageLabel,
            Customer = doc.Project.Customer,
            Designer = doc.Project.Designer,
            VatRate = doc.Project.VatRate,
            CurrentIndex = doc.Project.CurrentIndex,
            WorkFolder = dir,
            Survey = doc.Survey
        };
        File.WriteAllText(Path.Combine(dir, "project.json"), JsonSerializer.Serialize(payload, PirJson.Options));
        WriteDesktopIni(doc);
    }

    public static void Sync(PirDocument doc)
    {
        var dir = Require(doc);
        WriteProject(doc);
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var est in doc.Estimates)
        {
            var path = EstimatePath(dir, est);
            keep.Add(path);
            var payload = new EstimateFile { ProjectId = doc.Id, Estimate = est };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, PirJson.Options));
        }

        var folder = Path.Combine(dir, "estimates");
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.GetFiles(folder, "*.json"))
        {
            if (!keep.Contains(file)) File.Delete(file);
        }
    }

    public static void WriteEstimate(PirDocument doc, Estimate estimate)
    {
        var dir = Require(doc);
        var file = EstimatePath(dir, estimate);
        var payload = new EstimateFile { ProjectId = doc.Id, Estimate = estimate };
        File.WriteAllText(file, JsonSerializer.Serialize(payload, PirJson.Options));
    }

    public static PirDocument Load(string folder)
    {
        var projectPath = Path.Combine(folder, "project.json");
        if (!File.Exists(projectPath))
            throw new InvalidOperationException("В папке нет project.json");
        var file = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(projectPath), PirJson.Options)
                   ?? throw new InvalidOperationException("Пустой project.json");
        var doc = new PirDocument
        {
            Id = string.IsNullOrWhiteSpace(file.Id) ? Guid.NewGuid().ToString("N") : file.Id,
            Source = new SourceInfo { Kind = "folder", File = folder },
            Project = new ProjectHeader
            {
                ObjectName = file.ObjectName,
                Stage = Enum.TryParse<DesignStage>(file.Stage, true, out var stage) ? stage : DesignStage.WorkDoc,
                Customer = file.Customer,
                Designer = file.Designer,
                VatRate = file.VatRate,
                CurrentIndex = file.CurrentIndex,
                WorkFolder = folder
            },
            Survey = file.Survey
        };
        doc.Project.StageLabel = string.IsNullOrWhiteSpace(file.StageLabel) ? StageHelper.ToLabel(doc.Project.Stage) : file.StageLabel;
        Rescan(doc);
        return doc;
    }

    public static void Rescan(PirDocument doc)
    {
        var dir = Require(doc);
        WriteDesktopIni(doc);
        var estimates = new List<Estimate>();
        foreach (var file in Directory.GetFiles(Path.Combine(dir, "estimates"), "*.json")
                     .OrderBy(f => Path.GetFileNameWithoutExtension(f), Comparer<string>.Create(EstimateOutline.CompareNumbers)))
        {
            var payload = JsonSerializer.Deserialize<EstimateFile>(File.ReadAllText(file), PirJson.Options);
            if (payload?.Estimate is null) continue;
            estimates.Add(payload.Estimate);
        }
        doc.Estimates = estimates;
    }

    public static Estimate CopyEstimate(PirDocument doc, string estimateId)
    {
        var src = doc.Estimates.FirstOrDefault(e => e.Id == estimateId || e.Number == estimateId)
                  ?? throw new InvalidOperationException("Смета не найдена");
        var json = JsonSerializer.Serialize(src, PirJson.Options);
        var copy = JsonSerializer.Deserialize<Estimate>(json, PirJson.Options)
                   ?? throw new InvalidOperationException("Не удалось скопировать смету");
        copy.Id = Guid.NewGuid().ToString("N")[..12];
        copy.Title = src.Title + " копия";
        copy.Number = NextNumber(doc);
        doc.Estimates.Add(copy);
        WriteEstimate(doc, copy);
        return copy;
    }

    public static Estimate ImportEstimate(PirDocument target, Estimate source)
    {
        var copy = Clone(source);
        copy.Id = NewId();
        copy.OutlineDepth = 0;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pos in copy.Positions)
        {
            var next = NewId();
            if (!string.IsNullOrEmpty(pos.Id)) map[pos.Id] = next;
            pos.Id = next;
            pos.SysId = null;
        }
        foreach (var pos in copy.Positions)
        {
            if (!string.IsNullOrEmpty(pos.Section) && map.TryGetValue(pos.Section, out var mapped))
                pos.Section = mapped;
        }
        target.Estimates.Add(copy);
        EstimateOutline.Renumber(target);
        return copy;
    }

    public static Position ImportPosition(Estimate targetEst, Position source)
    {
        var copy = Clone(source);
        copy.Id = NewId();
        copy.SysId = null;
        copy.Section = "";
        copy.EstimateNumber = targetEst.Number;
        copy.SheetName = targetEst.Sheet;
        var at = targetEst.Positions.FindIndex(p => p.Kind is PositionKind.Total or PositionKind.Accrual);
        if (at < 0) targetEst.Positions.Add(copy);
        else targetEst.Positions.Insert(at, copy);
        return copy;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, PirJson.Options);
        return JsonSerializer.Deserialize<T>(json, PirJson.Options)
               ?? throw new InvalidOperationException("Не удалось скопировать");
    }

    public static bool DeleteEstimate(PirDocument doc, string estimateId)
    {
        var est = doc.Estimates.FirstOrDefault(e => e.Id == estimateId || e.Number == estimateId);
        if (est is null) return false;
        var path = EstimatePath(Require(doc), est);
        if (File.Exists(path)) File.Delete(path);
        doc.Estimates.Remove(est);
        return true;
    }

    public static void WriteDesktopIni(PirDocument doc)
    {
        var dir = Require(doc);
        var tip = $"ПИР · {doc.Project.StageLabel} · {doc.Project.Customer}".Trim(' ', '·');
        ShellFolder.ShowAs(dir, doc.Project.ObjectName, tip);
        NameSystemFolders(dir);
    }

    private static void NameSystemFolders(string dir)
    {
        ShellFolder.ShowAs(Path.Combine(dir, "estimates"), "Сметы", "Рабочие сметы");
        ShellFolder.ShowAs(Path.Combine(dir, "assignments"), "Задания", "Задания на проектирование");
        ShellFolder.ShowAs(Path.Combine(dir, "scope"), "Состав", "Состав работ");
        ShellFolder.ShowAs(Path.Combine(dir, "vor"), "Объёмы работ", "Ведомость объёмов работ");
    }

    public static IReadOnlyList<string> ListAssignments(PirDocument doc)
    {
        var dir = Path.Combine(Require(doc), "assignments");
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir).Select(Path.GetFileName).Where(n => n is not null).Cast<string>().ToList();
    }

    public static string ReadAssignmentText(PirDocument doc, string fileName)
    {
        var path = Path.Combine(Require(doc), "assignments", Path.GetFileName(fileName));
        if (!File.Exists(path)) throw new FileNotFoundException(fileName);
        var ext = Path.GetExtension(path);
        if (ext.Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return ReadDocx(path);
        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return ReadPdfText(path);
        return File.ReadAllText(path);
    }

    public static string ScopeDraftPath(PirDocument doc) => Path.Combine(Require(doc), "scope", "sostav.draft.json");

    public static void WriteScopeDraft(PirDocument doc, ScopeDraft draft)
    {
        Directory.CreateDirectory(Path.Combine(Require(doc), "scope"));
        File.WriteAllText(ScopeDraftPath(doc), JsonSerializer.Serialize(draft, PirJson.Options));
    }

    public static ScopeDraft? ReadScopeDraft(PirDocument doc)
    {
        var path = ScopeDraftPath(doc);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<ScopeDraft>(File.ReadAllText(path), PirJson.Options);
    }

    public static void WriteVorDraft(PirDocument doc, VorDraft draft)
    {
        var dir = Path.Combine(Require(doc), "vor");
        Directory.CreateDirectory(dir);
        var name = Sanitize(string.IsNullOrWhiteSpace(draft.Title) ? "vor" : draft.Title) + ".draft.json";
        File.WriteAllText(Path.Combine(dir, name), JsonSerializer.Serialize(draft, PirJson.Options));
    }

    public static IReadOnlyList<VorDraft> ReadVorDrafts(PirDocument doc)
    {
        var dir = Path.Combine(Require(doc), "vor");
        if (!Directory.Exists(dir)) return [];
        var list = new List<VorDraft>();
        foreach (var file in Directory.GetFiles(dir, "*.draft.json"))
        {
            var draft = JsonSerializer.Deserialize<VorDraft>(File.ReadAllText(file), PirJson.Options);
            if (draft is not null) list.Add(draft);
        }
        return list;
    }

    public static string ReadDocx(string path)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return "";
        using var reader = new StreamReader(entry.Open());
        var xml = reader.ReadToEnd();
        var text = Regex.Replace(xml, "</w:p>", "\n");
        text = Regex.Replace(text, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    public static string ReadPdfText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var latin = Encoding.Latin1.GetString(bytes);
        var runs = Regex.Matches(latin, @"\(([^\\)]{8,})\)")
            .Select(m => m.Groups[1].Value)
            .Where(s => s.Any(char.IsLetter))
            .Take(80);
        var text = string.Join(" ", runs).Trim();
        return text.Length > 0 ? text : $"PDF без текстового слоя ({bytes.Length} байт), OCR не выполняется.";
    }

    private static string EstimatePath(string dir, Estimate estimate)
    {
        var slug = Sanitize(string.IsNullOrWhiteSpace(estimate.Number) ? estimate.Id : estimate.Number);
        return Path.Combine(dir, "estimates", slug + ".json");
    }

    private static string NextNumber(PirDocument doc)
    {
        var n = doc.Estimates.Count + 1;
        return n.ToString();
    }

    public static string Require(PirDocument doc)
    {
        if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
            throw new InvalidOperationException("У объекта нет рабочей папки");
        return doc.Project.WorkFolder;
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(s) ? "item" : s;
    }

    public sealed class ProjectFile
    {
        public string Schema { get; set; } = "adept-project.v1";
        public string Id { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public string Stage { get; set; } = "WorkDoc";
        public string? StageLabel { get; set; }
        public string Customer { get; set; } = "";
        public string Designer { get; set; } = "";
        public decimal VatRate { get; set; } = 0.22m;
        public decimal? CurrentIndex { get; set; }
        public string? WorkFolder { get; set; }
        public SurveySheet? Survey { get; set; }
    }

    public sealed class EstimateFile
    {
        public string Schema { get; set; } = "adept-estimate.v1";
        public string ProjectId { get; set; } = "";
        public Estimate? Estimate { get; set; }
    }
}

public sealed class ScopeItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Section { get; set; }
    public string? RpdGroup { get; set; }
    public string? Stage { get; set; }
    public string? SourceQuote { get; set; }
    public string? RequirementRef { get; set; }
}

public sealed class ScopeDraft
{
    public string Status { get; set; } = "draft";
    public List<ScopeItem> Items { get; set; } = [];
}

public sealed class VorRow
{
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public string? SourceClause { get; set; }
    public string? ScopeItemId { get; set; }
}

public sealed class VorDraft
{
    public string Status { get; set; } = "draft";
    public string Title { get; set; } = "Ведомость объёмов";
    public List<VorRow> Rows { get; set; } = [];
}
