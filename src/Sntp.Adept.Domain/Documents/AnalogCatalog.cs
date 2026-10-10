using System.Text.Json;
using Sntp.Adept.Domain.Json;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

public sealed class AnalogCatalogFile
{
    public List<AnalogCard> Items { get; set; } = [];
}

public sealed class AnalogCard
{
    public string Id { get; set; } = "";
    public string SystemName { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string Format { get; set; } = "unknown";
    public long Size { get; set; }
    public string Modified { get; set; } = "";
    public string ImportedAt { get; set; } = "";
    public bool NameEdited { get; set; }
    public string Status { get; set; } = "unknown";
    public bool Stale { get; set; }
    public List<string> Sheets { get; set; } = [];
    /// <summary>Показатели файла: для Grand — сметы, разделы, расценки, стадия.</summary>
    public List<AnalogFact> Facts { get; set; } = [];
    /// <summary>Имена разделов Grand. Для Excel структура лежит в Sheets.</summary>
    public List<string> Parts { get; set; } = [];
}

public sealed class AnalogFact
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class AnalogRead
{
    public List<Estimate> Estimates { get; set; } = [];
    public List<AnalogRowScore> Scores { get; set; } = [];
}

public sealed class AnalogRowScore
{
    public string EstimateId { get; set; } = "";
    public string PositionId { get; set; } = "";
    public double Confidence { get; set; }
    public string Note { get; set; } = "";
    public bool LooksLikeBook { get; set; }
}

public sealed class AnalogMappings
{
    public List<AnalogMapRule> Rules { get; set; } = [];
}

public sealed class AnalogMapRule
{
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Role { get; set; } = "";
}

public static class AnalogCatalog
{
    public static string Root(PirDocument doc)
    {
        var dir = Path.Combine(WorkFolder.Require(doc), "analogs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static AnalogCatalogFile Load(PirDocument doc)
    {
        var path = Path.Combine(Root(doc), "catalog.json");
        if (!File.Exists(path))
        {
            var empty = new AnalogCatalogFile();
            WriteShellNames(doc, empty);
            return empty;
        }
        var file = JsonSerializer.Deserialize<AnalogCatalogFile>(File.ReadAllText(path), PirJson.Options) ?? new AnalogCatalogFile();
        foreach (var card in file.Items)
            card.Stale = IsStale(doc, card);
        WriteShellNames(doc, file);
        return file;
    }

    public static void Save(PirDocument doc, AnalogCatalogFile catalog)
    {
        File.WriteAllText(Path.Combine(Root(doc), "catalog.json"), JsonSerializer.Serialize(catalog, PirJson.Options));
        WriteShellNames(doc, catalog);
    }

    public static AnalogMappings LoadMaps(PirDocument doc)
    {
        var path = Path.Combine(Root(doc), "mappings.json");
        if (!File.Exists(path)) return new AnalogMappings();
        return JsonSerializer.Deserialize<AnalogMappings>(File.ReadAllText(path), PirJson.Options) ?? new AnalogMappings();
    }

    public static void SaveMaps(PirDocument doc, AnalogMappings maps)
    {
        File.WriteAllText(Path.Combine(Root(doc), "mappings.json"), JsonSerializer.Serialize(maps, PirJson.Options));
    }

    public static AnalogCard AddSource(PirDocument doc, string fileName, byte[] bytes)
    {
        var catalog = Load(doc);
        var id = Guid.NewGuid().ToString("N")[..8];
        var safe = SafeFile(fileName);
        var dir = Path.Combine(Root(doc), id);
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, safe);
        File.WriteAllBytes(source, bytes);
        var info = new FileInfo(source);
        var card = new AnalogCard
        {
            Id = id,
            SystemName = Path.GetFileNameWithoutExtension(safe),
            SourceName = fileName,
            SourceFile = safe,
            Format = FormatOf(safe),
            Size = info.Length,
            Modified = info.LastWriteTimeUtc.ToString("o"),
            ImportedAt = DateTimeOffset.UtcNow.ToString("o"),
            Status = "stored"
        };
        catalog.Items.Add(card);
        Save(doc, catalog);
        return card;
    }

    public static void ReplaceSource(PirDocument doc, AnalogCard card, byte[] bytes)
    {
        var path = SourcePath(doc, card);
        File.WriteAllBytes(path, bytes);
        var info = new FileInfo(path);
        card.Size = info.Length;
        card.Modified = info.LastWriteTimeUtc.ToString("o");
        card.ImportedAt = DateTimeOffset.UtcNow.ToString("o");
        card.Stale = false;
    }

    public static void WriteRead(PirDocument doc, string id, AnalogRead read)
    {
        var dir = Path.Combine(Root(doc), id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "read.json"), JsonSerializer.Serialize(read, PirJson.Options));
    }

    public static AnalogRead? Read(PirDocument doc, string id)
    {
        var path = Path.Combine(Root(doc), id, "read.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<AnalogRead>(File.ReadAllText(path), PirJson.Options);
    }

    public static AnalogCard? Find(PirDocument doc, string id) =>
        Load(doc).Items.FirstOrDefault(c => c.Id == id);

    public static void Remove(PirDocument doc, string id)
    {
        var catalog = Load(doc);
        catalog.Items.RemoveAll(c => c.Id == id);
        Save(doc, catalog);
        var dir = Path.Combine(Root(doc), id);
        if (Directory.Exists(dir))
        {
            ShellFolder.Release(dir);
            Directory.Delete(dir, true);
        }
    }

    public static string SourcePath(PirDocument doc, AnalogCard card) =>
        Path.Combine(Root(doc), card.Id, card.SourceFile);

    public static bool IsStale(PirDocument doc, AnalogCard card)
    {
        var path = SourcePath(doc, card);
        if (!File.Exists(path)) return true;
        var info = new FileInfo(path);
        if (info.Length != card.Size) return true;
        if (!DateTimeOffset.TryParse(card.Modified, out var stored)) return true;
        return Math.Abs((info.LastWriteTimeUtc - stored.UtcDateTime).TotalSeconds) > 2;
    }

    public static void Touch(PirDocument doc, AnalogCard card)
    {
        var catalog = Load(doc);
        var at = catalog.Items.FindIndex(c => c.Id == card.Id);
        if (at < 0) catalog.Items.Add(card);
        else catalog.Items[at] = card;
        Save(doc, catalog);
    }

    public static string FormatOf(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) || ext.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) || ext.Equals(".xls", StringComparison.OrdinalIgnoreCase))
            return "excel";
        if (ext.Equals(".xml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".gsfx", StringComparison.OrdinalIgnoreCase))
            return "grand";
        return "unknown";
    }

    private static string SafeFile(string fileName)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void WriteShellNames(PirDocument doc, AnalogCatalogFile catalog)
    {
        var root = Root(doc);
        WriteIni(root, "Аналоги", "Исходные сметы Excel и Grand. В рабочие сметы не входят, пока их не примут.");
        foreach (var card in catalog.Items)
        {
            var dir = Path.Combine(root, card.Id);
            if (!Directory.Exists(dir)) continue;
            var title = string.IsNullOrWhiteSpace(card.SystemName) ? card.SourceName : card.SystemName;
            var tip = string.IsNullOrWhiteSpace(card.SourceName) ? card.Format : card.SourceName;
            WriteIni(dir, title, tip);
        }
    }

    private static void WriteIni(string dir, string name, string tip) =>
        ShellFolder.ShowAs(dir, name, tip);
}

public static class AnalogOutline
{
    public static void Fill(AnalogCard card, IReadOnlyList<Estimate> estimates, string? stage, string? region, string? program)
    {
        var positions = estimates.SelectMany(e => e.Positions).ToList();
        var parts = positions
            .Where(p => p.Kind == PositionKind.Section && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => p.Name)
            .ToList();
        if (parts.Count == 0)
            parts = estimates
                .Select(e => e.ChapterCaption)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Distinct()
                .ToList();
        var works = positions.Count(p => p.Kind == PositionKind.Work);
        var factors = estimates.Sum(e => e.Factors.Count);
        card.Parts = parts;
        card.Facts =
        [
            new AnalogFact { Label = "Смет", Value = estimates.Count.ToString() },
            new AnalogFact { Label = "Разделов", Value = Math.Max(parts.Count, estimates.Count).ToString() },
            new AnalogFact { Label = "Расценок", Value = works.ToString() },
            new AnalogFact { Label = "Коэффициентов", Value = factors.ToString() }
        ];
        if (!string.IsNullOrWhiteSpace(stage))
            card.Facts.Add(new AnalogFact { Label = "Стадия", Value = stage });
        if (!string.IsNullOrWhiteSpace(region))
            card.Facts.Add(new AnalogFact { Label = "Регион", Value = region });
        if (!string.IsNullOrWhiteSpace(program))
            card.Facts.Add(new AnalogFact { Label = "Гранд", Value = program });
    }
}
