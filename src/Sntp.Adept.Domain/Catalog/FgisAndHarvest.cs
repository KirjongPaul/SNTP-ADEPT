using System.Globalization;
using System.Text.Json;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Catalog;

public sealed class FgisPeriod
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class FgisSnRow
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Cipher { get; set; }
    public string? OrderRef { get; set; }
    public int? PeriodId { get; set; }
}

public sealed class FgisIndexRow
{
    public int? PeriodId { get; set; }
    public string? Name { get; set; }
    public decimal? Value { get; set; }
    public string? Quarter { get; set; }
    public string? Year { get; set; }
    public string? BaseYear { get; set; }
}

public static class FgisCsParser
{
    public static IReadOnlyList<FgisPeriod> ParsePeriods(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];
        var active = root.TryGetProperty("activePeriodId", out var act) && act.TryGetInt32(out var id) ? id : (int?)null;
        var list = new List<FgisPeriod>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("value", out var value) || !value.TryGetInt32(out var pid)) continue;
            list.Add(new FgisPeriod
            {
                Id = pid,
                Text = item.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "",
                Active = active == pid
            });
        }
        return list;
    }

    public static bool IsEmptyRegistry(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;
        var docs = CountArray(root, "documents");
        var hier = CountArray(root, "hierarchy");
        return docs == 0 && hier == 0;
    }

    public static IReadOnlyList<FgisSnRow> ParseSn(string json, string kind, int? periodId)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var list = new List<FgisSnRow>();
        Walk(doc.RootElement, kind, periodId, list);
        return list
            .GroupBy(r => (r.Name, r.OrderRef ?? ""))
            .Select(g => g.First())
            .ToList();
    }

    public static IReadOnlyList<FgisIndexRow> ParseIndexes(string json, int? periodId)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var list = new List<FgisIndexRow>();
        WalkIndexes(doc.RootElement, periodId, null, list);
        return list;
    }

    private static void Walk(JsonElement el, string kind, int? periodId, List<FgisSnRow> list)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            var name = Str(el, "name");
            if (!string.IsNullOrWhiteSpace(name) && (el.TryGetProperty("approvingActInfo", out _) || el.TryGetProperty("guid", out _) || el.TryGetProperty("filePath", out _)))
            {
                list.Add(new FgisSnRow
                {
                    Kind = kind,
                    Name = name!,
                    Cipher = BasisParser.Parse(name).CanonicalDoc,
                    OrderRef = Str(el, "approvingActInfo"),
                    PeriodId = periodId
                });
            }

            foreach (var prop in el.EnumerateObject())
                Walk(prop.Value, kind, periodId, list);
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                Walk(item, kind, periodId, list);
        }
    }

    private static void WalkIndexes(JsonElement el, int? periodId, string? letter, List<FgisIndexRow> list)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            var name = Str(el, "name") ?? letter;
            if (el.TryGetProperty("indexes", out var indexes) && indexes.ValueKind == JsonValueKind.Array)
            {
                foreach (var idx in indexes.EnumerateArray())
                    AddIndex(idx, periodId, name, list);
            }

            AddIndex(el, periodId, name, list);
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.NameEquals("indexes")) continue;
                WalkIndexes(prop.Value, periodId, name, list);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                WalkIndexes(item, periodId, letter, list);
        }
    }

    private static void AddIndex(JsonElement el, int? periodId, string? name, List<FgisIndexRow> list)
    {
        decimal? value = Dec(el, "estimatedPriceSurveyDesignForBuildingIndex")
                         ?? Dec(el, "estimatedPriceSurveyWorkForBuildingIndex")
                         ?? Dec(el, "index")
                         ?? Dec(el, "value");
        if (value is null) return;
        var level = Str(el, "rtmPriceLevel");
        list.Add(new FgisIndexRow
        {
            PeriodId = periodId,
            Name = name ?? level,
            Value = value,
            BaseYear = level,
            Quarter = QuarterOf(name),
            Year = YearOf(name)
        });
    }

    private static string? QuarterOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"([1-4])\s*квартал", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? YearOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(20\d{2})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int CountArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Array ? el.GetArrayLength() : 0;

    private static string? Str(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static decimal? Dec(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }
}

public static class GgeGuard
{
    public static bool IsGge(string? fileName, string? rootName) =>
        fileName?.EndsWith(".gge", StringComparison.OrdinalIgnoreCase) == true
        || string.Equals(rootName, "Construction", StringComparison.OrdinalIgnoreCase);

    public static string RejectMessage =>
        "gge-not-pir: XML Минстроя (корень Construction, файл .gge) не является сметой на ПИР.";
}

public static class AdeptUiRate
{
    public sealed class HarvestRow
    {
        public string GroupPath { get; set; } = "";
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Unit { get; set; }
        public string? Multiplier { get; set; }
        public decimal? PriceA { get; set; }
        public decimal? PriceB { get; set; }
        public string? ParentCode { get; set; }
        public bool Qualifier { get; set; }
        public Basis Basis { get; set; } = new();
    }

    public static HarvestRow FromGrid(string groupPath, string code, string name, string? unit, string? multiplier, decimal? priceA, decimal? priceB, string? parentCode = null)
    {
        var basis = BasisParser.Parse(code);
        var qualifier = priceA is null && priceB is null && !string.IsNullOrWhiteSpace(parentCode);
        return new HarvestRow
        {
            GroupPath = groupPath,
            Code = code,
            Name = name,
            Unit = unit,
            Multiplier = multiplier,
            PriceA = priceA,
            PriceB = priceB,
            ParentCode = qualifier ? parentCode : ParentOf(basis),
            Qualifier = qualifier || !string.IsNullOrWhiteSpace(basis.Chapter) && basis.Kind == "SBCI" && priceA is null,
            Basis = basis
        };
    }

    public static string? ParentOf(Basis basis)
    {
        if (basis.Kind == "SBCI" && !string.IsNullOrWhiteSpace(basis.AdeptCode) && !string.IsNullOrWhiteSpace(basis.Chapter))
        {
            var code = basis.AdeptCode!;
            var tail = "-" + basis.Chapter;
            if (code.EndsWith(tail, StringComparison.Ordinal))
                return code[..^tail.Length];
        }
        return null;
    }
}
