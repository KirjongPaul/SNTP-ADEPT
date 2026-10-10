using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Catalog;

public sealed class AprParseResult
{
    public string FileName { get; init; } = "";
    public string? Encoding { get; init; }
    public string? Root { get; init; }
    public string? Version { get; init; }
    public List<CatalogKoeff> Koeffs { get; } = [];
    public List<CatalogIndex> Indexes { get; } = [];
    public List<CatalogRate> Rates { get; } = [];
    public List<CatalogRpd> Rpds { get; } = [];
    public List<CatalogStruct> Structs { get; } = [];
    public string? Error { get; init; }
}

public sealed class CatalogKoeff
{
    public string SourceFile { get; set; } = "";
    public string? GroupName { get; set; }
    public string? ExternalId { get; set; }
    public string Name { get; set; } = "";
    public string? TypeName { get; set; }
    public int? TypeNum { get; set; }
    public string CombineType { get; set; } = "pricing";
    public decimal? Value { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public bool HasRange { get; set; }
    public string? Formula { get; set; }
    public string? Status { get; set; }
    public string? PromoType { get; set; }
}

public sealed class CatalogIndex
{
    public string SourceFile { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Value { get; set; }
    public string? CurrentQuarter { get; set; }
    public string? CurrentYear { get; set; }
    public string? BaseYear { get; set; }
    public string? TypeOfWork { get; set; }
    public string? Office { get; set; }
    public string? GroupName { get; set; }
}

public sealed class CatalogRate
{
    public string SourceFile { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Doc { get; set; }
    public int? Year { get; set; }
    public string? Table { get; set; }
    public string? Item { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? PriceA { get; set; }
    public decimal? PriceB { get; set; }
    public string? Unit { get; set; }
    public string Source { get; set; } = "apr";
    public string? Raw { get; set; }
}

public sealed class CatalogRpd
{
    public string SourceFile { get; set; } = "";
    public string? Folder { get; set; }
    public string? GroupName { get; set; }
    public string Name { get; set; } = "";
    public string? ParentName { get; set; }
    public decimal? Share { get; set; }
}

public sealed class CatalogStruct
{
    public string SourceFile { get; set; } = "";
    public string? Folder { get; set; }
    public string? GroupName { get; set; }
    public string Name { get; set; } = "";
    public decimal? Value { get; set; }
}

public static class AprXml
{
    public static readonly string[] DefaultImportFiles =
    [
        "koeffs.apr",
        "koeffs2.apr",
        "indexes.apr",
        "type_rpds.apr",
        "type_sostav.apr",
        "type_struct.apr",
        "smet_templates_type.apr",
        "ShortRascNames.apr",
        "planKoeffs.apr",
        "partsObjects.apr"
    ];

    public static AprParseResult LoadFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return LoadBytes(bytes, Path.GetFileName(path));
    }

    public static AprParseResult LoadBytes(byte[] bytes, string fileName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encodingName = DetectEncoding(bytes) ?? "windows-1251";
        Encoding enc;
        try { enc = Encoding.GetEncoding(encodingName); }
        catch { enc = Encoding.GetEncoding(1251); }

        var text = enc.GetString(bytes);
        text = StripDoctype(text);
        try
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var root = doc.Root ?? throw new InvalidOperationException("Пустой XML");
            var result = new AprParseResult
            {
                FileName = fileName,
                Encoding = encodingName,
                Root = root.Name.LocalName,
                Version = Attr(root, "version")
            };
            Fill(root, result, fileName);
            return result;
        }
        catch (Exception ex)
        {
            return new AprParseResult { FileName = fileName, Encoding = encodingName, Error = ex.Message };
        }
    }

    public static IReadOnlyList<AprParseResult> LoadDirectory(string directory, IEnumerable<string>? files = null)
    {
        var list = new List<AprParseResult>();
        if (!Directory.Exists(directory)) return list;
        var names = (files ?? DefaultImportFiles).ToList();
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            // Не трогаем СМР-индексы на десятки мегабайт.
            if (name.Contains("SMR", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.Contains("konc", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(LoadFile(path));
        }
        return list;
    }

    public static string? MatchIndexName(IEnumerable<CatalogIndex> indexes, string? caption, decimal? value)
    {
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var hit = indexes.FirstOrDefault(i =>
                !string.IsNullOrEmpty(i.Name) &&
                (caption.Contains(i.Name, StringComparison.OrdinalIgnoreCase)
                 || i.Name.Contains(caption.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (hit is not null) return hit.Name;
        }
        if (value is decimal v)
        {
            var hit = indexes.FirstOrDefault(i => i.Value is decimal d && Math.Abs(d - v) < 0.0005m);
            if (hit is not null) return hit.Name;
        }
        return null;
    }

    public static string RateCode(Basis? b)
    {
        if (b is null) return "";
        if (b.Kind is "SBCI" && !string.IsNullOrWhiteSpace(b.AdeptCode))
            return b.AdeptCode!;
        if (b.Kind is "SBC" or "SBCP"
            && b.CollectionNum is int nn
            && b.CanonicalDoc?.Contains("81-2001", StringComparison.Ordinal) == true)
        {
            var parts = new List<string> { "SBC", "81", "2001", nn.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            var table = CleanRef(b.Table);
            var item = CleanRef(b.Item);
            if (table.Length > 0) parts.Add(table);
            if (item.Length > 0) parts.Add(item);
            return string.Join("-", parts);
        }

        if (b.ParsePattern == "grand-compact" && b.CollectionNum is int gnn)
            return string.Join("-", new[] { "SBC", gnn.ToString(System.Globalization.CultureInfo.InvariantCulture), CleanRef(b.Table), CleanRef(b.Item) }.Where(s => s.Length > 0));

        if (b.Kind == "MRR" && !string.IsNullOrWhiteSpace(b.CanonicalDoc))
            return "MRR-" + CleanRef(b.Doc);

        var kind = string.IsNullOrWhiteSpace(b.Kind) ? "X" : b.Kind.ToUpperInvariant();
        var doc = CleanRef((b.Doc ?? "")
            .Replace("/pr", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/пр", "", StringComparison.OrdinalIgnoreCase));
        return string.Join("-", new[] { kind, doc, CleanRef(b.Table), CleanRef(b.Item) }.Where(s => s.Length > 0));
    }

    public static string CleanRef(string? s) => (s ?? "").Trim().TrimEnd('.').Trim();

    public static CatalogRate? FromBasis(Basis b, string name, decimal? a, decimal? bPrice, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(b.Raw) && string.IsNullOrWhiteSpace(b.Doc)) return null;
        return new CatalogRate
        {
            SourceFile = sourceFile,
            Kind = b.Kind,
            Doc = b.Doc,
            Year = b.Year,
            Table = CleanRef(b.Table),
            Item = CleanRef(b.Item),
            Code = RateCode(b),
            Name = name,
            PriceA = a,
            PriceB = bPrice,
            Source = "excel-seed",
            Raw = b.Raw
        };
    }

    internal static string StripDoctype(string text)
    {
        text = Regex.Replace(text, @"<!DOCTYPE[\s\S]*?\]>", "", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        return text;
    }

    private static string? DetectEncoding(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 180));
        var m = Regex.Match(head, @"encoding\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static void Fill(XElement root, AprParseResult result, string file)
    {
        var local = root.Name.LocalName;
        if (local is "main" || local.Contains("Koeff", StringComparison.OrdinalIgnoreCase) || file.StartsWith("koeffs", StringComparison.OrdinalIgnoreCase))
            ReadKoeffs(root, result, file);
        if (local is "ind" || file.StartsWith("indexes", StringComparison.OrdinalIgnoreCase))
            ReadIndexes(root, result, file);
        if (local is "TypeRpds" || file.Contains("type_rpd", StringComparison.OrdinalIgnoreCase) || file.Contains("type_sostav", StringComparison.OrdinalIgnoreCase))
            ReadRpds(root, result, file);
        if (local is "root" && file.Contains("type_struct", StringComparison.OrdinalIgnoreCase))
            ReadStructs(root, result, file);
        if (file.Contains("ShortRasc", StringComparison.OrdinalIgnoreCase))
            ReadShortNames(root, result, file);
        if (file.Contains("smet_templates", StringComparison.OrdinalIgnoreCase))
            ReadTemplateHint(root, result, file);
    }

    private static void ReadKoeffs(XElement root, AprParseResult result, string file)
    {
        foreach (var group in root.Descendants().Where(e => e.Name.LocalName is "koeffs" or "RootKoeffs"))
        {
            var groupName = Attr(group, "Name");
            var promo = Attr(group, "PromoType");
            foreach (var el in group.Elements().Where(e => e.Name.LocalName is "koeff" or "koefficient"))
            {
                var typeName = Attr(el, "Type");
                var typeNum = Int(el, "TypeNum");
                result.Koeffs.Add(new CatalogKoeff
                {
                    SourceFile = file,
                    GroupName = groupName,
                    PromoType = promo,
                    ExternalId = Attr(el, "ID"),
                    Name = Attr(el, "Name") ?? "",
                    TypeName = typeName,
                    TypeNum = typeNum,
                    CombineType = FactorBinder.FromAdeptType(typeName, typeNum).ToString(),
                    Value = Dec(el, "Dolya") ?? Dec(el, "Value"),
                    MinValue = Dec(el, "MinDolya") ?? Dec(el, "min_value"),
                    MaxValue = Dec(el, "MaxDolya") ?? Dec(el, "max_value"),
                    HasRange = On(el, "HasRange"),
                    Formula = Attr(el, "Formula"),
                    Status = Attr(el, "Status")
                });
            }
        }
    }

    private static void ReadIndexes(XElement root, AprParseResult result, string file)
    {
        string? group = null;
        foreach (var el in root.Descendants())
        {
            if (el.Name.LocalName == "g")
                group = Attr(el, "Name");
            if (el.Name.LocalName != "i") continue;
            result.Indexes.Add(new CatalogIndex
            {
                SourceFile = file,
                GroupName = group,
                Name = Attr(el, "Name") ?? "",
                Value = Dec(el, "Dolya"),
                CurrentQuarter = Attr(el, "currentQuarter"),
                CurrentYear = Attr(el, "currentYear"),
                BaseYear = Attr(el, "baseYear"),
                TypeOfWork = Attr(el, "typeOfWork"),
                Office = Attr(el, "office")
            });
        }
    }

    private static void ReadRpds(XElement root, AprParseResult result, string file)
    {
        string? folder = null, group = null;
        foreach (var el in root.Descendants())
        {
            var n = el.Name.LocalName;
            if (n is "Folder") folder = Attr(el, "Name");
            if (n is "RpdGroup" or "sostav") group = Attr(el, "Name");
            if (n is "Rpd" or "RPD" or "SubRpd")
            {
                result.Rpds.Add(new CatalogRpd
                {
                    SourceFile = file,
                    Folder = folder,
                    GroupName = group,
                    Name = Attr(el, "Name") ?? el.Value.Trim(),
                    ParentName = n == "SubRpd" ? el.Parent is null ? group : Attr(el.Parent, "Name") : group,
                    Share = Dec(el, "Dolya")
                });
            }
        }
    }

    private static void ReadStructs(XElement root, AprParseResult result, string file)
    {
        string? folder = null, group = null;
        foreach (var el in root.Descendants())
        {
            if (el.Name.LocalName == "Folder") folder = Attr(el, "Name");
            if (el.Name.LocalName is "structGroup") group = Attr(el, "Name");
            if (el.Name.LocalName is "Struct")
            {
                result.Structs.Add(new CatalogStruct
                {
                    SourceFile = file,
                    Folder = folder,
                    GroupName = group,
                    Name = Attr(el, "Name") ?? "",
                    Value = Dec(el, "Value")
                });
            }
        }
    }

    private static void ReadShortNames(XElement root, AprParseResult result, string file)
    {
        foreach (var el in root.Descendants("item"))
        {
            var full = Attr(el, "FullName") ?? "";
            var shortName = Attr(el, "ShortName") ?? "";
            result.Rates.Add(new CatalogRate
            {
                SourceFile = file,
                Kind = "SBC",
                Code = shortName,
                Name = full,
                Doc = full,
                Source = "apr-shortname",
                Raw = $"{full}={shortName}"
            });
        }
    }

    private static void ReadTemplateHint(XElement root, AprParseResult result, string file)
    {
        foreach (var el in root.Descendants("smeta"))
        {
            result.Rates.Add(new CatalogRate
            {
                SourceFile = file,
                Kind = "TEMPLATE",
                Code = Attr(el, "Typ2") ?? Attr(el, "Typ") ?? "template",
                Name = Attr(el, "templateName") ?? Attr(el, "Name") ?? "шаблон сметы",
                Source = "apr-template",
                Raw = Attr(el, "Stady")
            });
        }
    }

    private static string? Attr(XElement el, string name) =>
        el.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static decimal? Dec(XElement el, string name)
    {
        var v = Attr(el, name);
        return NumberParse.TryParse(v);
    }

    private static int? Int(XElement el, string name)
    {
        var v = Attr(el, name);
        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        return null;
    }

    private static bool On(XElement el, string name)
    {
        var v = Attr(el, name);
        return v is "On" or "1" or "true" or "True";
    }
}
