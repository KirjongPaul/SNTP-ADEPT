using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Paste;

public enum PasteRowKind
{
    DocumentHeader,
    ColumnMap,
    Section,
    Text,
    Work,
    Accrual,
    Total
}

public sealed class PasteCell
{
    public string Text { get; set; } = "";
}

public sealed class PasteRow
{
    public PasteRowKind Kind { get; set; }
    public List<string> Cells { get; set; } = [];
    public string? SuggestedName { get; set; }
    public string? SuggestedBasis { get; set; }
}

public sealed class PastePreview
{
    public string Format { get; set; } = "tsv";
    public List<string> ColumnMap { get; set; } = [];
    public List<PasteRow> Rows { get; set; } = [];
}

public static class ClipboardGridParser
{
    public static PastePreview Parse(string? text, string? html = null)
    {
        var source = !string.IsNullOrWhiteSpace(html) && html.Contains("<table", StringComparison.OrdinalIgnoreCase)
            ? html
            : text ?? "";
        List<List<string>> grid;
        string format;
        if (source.Contains("urn:schemas-microsoft-com:office:spreadsheet", StringComparison.OrdinalIgnoreCase)
            || source.Contains("<Workbook", StringComparison.OrdinalIgnoreCase))
        {
            format = "xml-spreadsheet";
            grid = ReadXmlSpreadsheet(source);
        }
        else if (source.Contains("<table", StringComparison.OrdinalIgnoreCase))
        {
            format = "html";
            grid = ReadHtml(source);
        }
        else
        {
            format = "tsv";
            grid = ReadTsv(text ?? "");
        }

        var preview = new PastePreview { Format = format };
        if (grid.Count == 0) return preview;

        var headerIndex = grid.FindIndex(IsColumnMap);
        if (headerIndex >= 0)
        {
            preview.ColumnMap = grid[headerIndex].Select(SuggestColumn).ToList();
            preview.Rows.Add(new PasteRow { Kind = PasteRowKind.ColumnMap, Cells = grid[headerIndex] });
        }

        for (var i = 0; i < grid.Count; i++)
        {
            if (i == headerIndex) continue;
            var cells = grid[i];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var kind = Classify(cells, i == 0 && headerIndex < 0);
            var row = new PasteRow { Kind = kind, Cells = cells };
            FillSuggestions(row, preview.ColumnMap);
            preview.Rows.Add(row);
        }

        return preview;
    }

    public static List<Position> ToPositions(PastePreview preview)
    {
        var list = new List<Position>();
        foreach (var row in preview.Rows)
        {
            if (row.Kind is PasteRowKind.ColumnMap or PasteRowKind.DocumentHeader) continue;
            var pos = new Position
            {
                Name = row.SuggestedName ?? FirstText(row.Cells),
                Origin = OriginKind.Created,
                Kind = row.Kind switch
                {
                    PasteRowKind.Section => PositionKind.Section,
                    PasteRowKind.Text => PositionKind.Text,
                    PasteRowKind.Accrual => PositionKind.Accrual,
                    PasteRowKind.Total => PositionKind.Total,
                    _ => PositionKind.Work
                }
            };
            if (pos.Kind == PositionKind.Work)
            {
                var basisText = row.SuggestedBasis ?? row.Cells.FirstOrDefault(c => BasisParser.Parse(c).Kind != "Unknown");
                if (!string.IsNullOrWhiteSpace(basisText))
                {
                    pos.Basis = BasisParser.Parse(basisText);
                    pos.CatalogCode = AprXml.RateCode(pos.Basis);
                    pos.Code = pos.Basis.AdeptCode ?? pos.Code;
                }
            }
            list.Add(pos);
        }
        return list;
    }

    private static void FillSuggestions(PasteRow row, List<string> map)
    {
        if (map.Count == 0)
        {
            row.SuggestedName = row.Cells.FirstOrDefault(c => c.Length > 3 && BasisParser.Parse(c).Kind == "Unknown");
            row.SuggestedBasis = row.Cells.FirstOrDefault(c => BasisParser.Parse(c).Kind != "Unknown");
            return;
        }

        for (var i = 0; i < row.Cells.Count && i < map.Count; i++)
        {
            if (map[i] is "name" or "наименование") row.SuggestedName = row.Cells[i];
            if (map[i] is "basis" or "обоснование") row.SuggestedBasis = row.Cells[i];
        }
    }

    private static PasteRowKind Classify(List<string> cells, bool first)
    {
        var joined = string.Join(" ", cells);
        if (IsColumnMap(cells)) return PasteRowKind.ColumnMap;
        if (joined.Contains("итого", StringComparison.OrdinalIgnoreCase) || joined.Contains("всего", StringComparison.OrdinalIgnoreCase))
            return PasteRowKind.Total;
        if (joined.Contains("начислен", StringComparison.OrdinalIgnoreCase))
            return PasteRowKind.Accrual;
        var filled = cells.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        if (filled.Count <= 1 && filled.Count == 1 && !LooksLikeMoney(filled[0]) && BasisParser.Parse(filled[0]).Kind == "Unknown")
            return first ? PasteRowKind.DocumentHeader : PasteRowKind.Section;
        if (BasisParser.Parse(joined).Kind != "Unknown")
            return PasteRowKind.Work;
        if (filled.Count == 1) return PasteRowKind.Text;
        return PasteRowKind.Work;
    }

    private static bool IsColumnMap(List<string> cells)
    {
        var hits = cells.Count(c => SuggestColumn(c) is not ("text"));
        return hits >= 2 && cells.Count <= 12;
    }

    public static string SuggestColumn(string header)
    {
        var h = header.Trim().ToLowerInvariant();
        if (h is "№" or "n" or "номер" or "п/п") return "number";
        if (h.Contains("наимен")) return "name";
        if (h.Contains("обоснова") || h.Contains("шифр")) return "basis";
        if (h is "a" || h.StartsWith("а ")) return "a";
        if (h is "b" || h.StartsWith("в ") || h == "b") return "b";
        if (h.Contains("колич") || h == "x") return "x";
        if (h.Contains("руб")) return "money";
        return "text";
    }

    private static bool LooksLikeMoney(string text) =>
        Regex.IsMatch(text, @"\d[\d\s]*[.,]\d{2}") && text.Contains("руб", StringComparison.OrdinalIgnoreCase);

    private static string FirstText(List<string> cells) =>
        cells.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim() ?? "";

    private static List<List<string>> ReadTsv(string text)
    {
        var rows = new List<List<string>>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Contains('\t') ? line.Split('\t') : line.Split(';');
            rows.Add(parts.Select(s => (WebUtility.HtmlDecode(s) ?? "").Trim()).ToList());
        }
        return rows;
    }

    private static List<List<string>> ReadHtml(string html)
    {
        var rows = new List<List<string>>();
        foreach (Match tr in Regex.Matches(html, @"<tr\b[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var cells = Regex.Matches(tr.Groups[1].Value, @"<t[dh]\b[^>]*>(.*?)</t[dh]>", RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(m => CleanHtml(m.Groups[1].Value))
                .ToList();
            if (cells.Count > 0) rows.Add(cells);
        }
        return rows;
    }

    private static List<List<string>> ReadXmlSpreadsheet(string xml)
    {
        var rows = new List<List<string>>();
        var doc = new XmlDocument();
        try
        {
            doc.LoadXml(xml);
        }
        catch
        {
            return rows;
        }

        foreach (XmlElement row in doc.GetElementsByTagName("Row"))
        {
            var cells = new List<string>();
            foreach (XmlElement cell in row.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "Cell"))
            {
                var data = cell.GetElementsByTagName("Data");
                cells.Add(data.Count > 0 ? data[0]!.InnerText.Trim() : cell.InnerText.Trim());
            }
            if (cells.Count > 0) rows.Add(cells);
        }
        return rows;
    }

    private static string CleanHtml(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(Regex.Replace(text, @"\s+", " ")).Trim();
    }
}
