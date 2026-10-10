using System.Globalization;
using System.Text.RegularExpressions;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Catalog;

/// <summary>
/// Разбор строки обоснования в <see cref="Basis"/>.
/// Порядок: длинный СБЦП 81-02, короткий 81-2001, суффикс Адепт, компакт Гранда,
/// шифр изысканий СБЦи, НЗ, МРР, приказ N/пр, алиас АСУТП.
/// </summary>
public static partial class BasisParser
{
    public static Basis Parse(string? raw)
    {
        var original = raw ?? "";
        var text = Normalize(original);
        if (text.Length == 0)
            return new Basis { Kind = "Unknown", Raw = original, ParsePattern = "empty" };

        var basis = TryLong(text, original)
                    ?? TryShort(text, original)
                    ?? TryAdeptSuffix(text, original)
                    ?? TryGrandCompact(text, original)
                    ?? TrySurvey(text, original)
                    ?? TryNz(text, original)
                    ?? TryMrr(text, original)
                    ?? TryOrder(text, original)
                    ?? TryAsutp(text, original);

        if (basis is null)
        {
            basis = new Basis { Kind = "Unknown", Raw = original, ParsePattern = "fallback" };
            FillTableItem(basis, text);
        }

        basis.Raw = original;
        return basis;
    }

    public static string Normalize(string raw)
    {
        var s = raw.Replace('ё', 'е').Replace('Ё', 'Е');
        s = LatinSbcp().Replace(s, "СБЦП");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static Basis? TryLong(string text, string original)
    {
        var m = LongSbcp().Match(text);
        if (!m.Success) return null;
        var nn = int.Parse(m.Groups["nn"].Value, CultureInfo.InvariantCulture);
        var b = StateSbc(nn, original, "long");
        if (int.TryParse(m.Groups["year"].Value, out var year)) b.Year = year;
        ApplySuffix(b, m);
        FillTableItem(b, text);
        b.AdeptCode = CompactAdept(text, m);
        return b;
    }

    private static Basis? TryShort(string text, string original)
    {
        var m = ShortSbcp().Match(text);
        if (!m.Success) return null;
        var nn = int.Parse(m.Groups["nn"].Value, CultureInfo.InvariantCulture);
        var b = StateSbc(nn, original, "short");
        ApplySuffix(b, m);
        FillTableItem(b, text);
        b.AdeptCode = CompactAdept(text, m);
        return b;
    }

    private static Basis? TryAdeptSuffix(string text, string original)
    {
        var m = SuffixOnly().Match(text);
        if (!m.Success) return null;
        var head = m.Groups["head"].Value.Trim();
        var nested = TryLong(head, original) ?? TryShort(head, original);
        var b = nested ?? new Basis
        {
            Kind = "SBC",
            Raw = original,
            Doc = head,
            CanonicalDoc = head,
            ParsePattern = "adept-suffix"
        };
        b.ParsePattern = "adept-suffix";
        ApplySuffix(b, m);
        b.AdeptCode = Normalize(m.Value);
        return b;
    }

    private static Basis? TryGrandCompact(string text, string original)
    {
        var m = GrandCompact().Match(text);
        if (!m.Success) return null;
        var nn = int.Parse(m.Groups["nn"].Value, CultureInfo.InvariantCulture);
        if (nn == 81) return null;
        var table = m.Groups["table"].Value;
        var item = m.Groups["item"].Value;
        return new Basis
        {
            Kind = "SBC",
            Raw = original,
            Doc = $"СБЦП-{nn}",
            CanonicalDoc = $"СБЦП-{nn}",
            CollectionNum = nn,
            Table = AprXml.CleanRef(table),
            Item = AprXml.CleanRef(item),
            AdeptCode = $"СБЦП{nn}-{table}-{item}",
            ParsePattern = "grand-compact"
        };
    }

    private static Basis? TrySurvey(string text, string original)
    {
        var m = SurveyCode().Match(text);
        if (!m.Success) return null;
        var ver = m.Groups["ver"].Value;
        var rest = m.Groups["rest"].Value;
        var parts = rest.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var b = new Basis
        {
            Kind = "SBCI",
            Raw = original,
            Doc = "СБЦи" + ver,
            CanonicalDoc = "СБЦи" + ver,
            AdeptCode = "СБЦи" + ver + "_" + rest + (m.Groups["n"].Success ? "(" + m.Groups["n"].Value + ")" : ""),
            ParsePattern = "sbci"
        };
        if (parts.Length > 0) b.Part = parts[0];
        if (parts.Length > 1) b.Table = parts[1];
        if (parts.Length > 2) b.Item = parts[2];
        if (parts.Length > 3) b.Chapter = string.Join("-", parts.Skip(3));
        if (m.Groups["n"].Success && string.IsNullOrEmpty(b.Chapter))
            b.Chapter = m.Groups["n"].Value;
        return b;
    }

    private static Basis? TryNz(string text, string original)
    {
        var doc = NzDoc().Match(text);
        if (!doc.Success) return null;
        var b = new Basis
        {
            Kind = "NZ",
            Raw = original,
            Doc = doc.Groups[1].Value + "/pr",
            ParsePattern = "nz"
        };
        var year = YearWord().Match(text);
        if (year.Success && int.TryParse(year.Groups[1].Value, out var y)) b.Year = y;
        FillTableItem(b, text);
        return b;
    }

    private static Basis? TryMrr(string text, string original)
    {
        var m = MrrCode().Match(text);
        if (!m.Success) return null;
        var year2 = int.Parse(m.Groups["yy"].Value, CultureInfo.InvariantCulture);
        return new Basis
        {
            Kind = "MRR",
            Raw = original,
            Doc = m.Groups["doc"].Value + "-" + m.Groups["yy"].Value,
            CanonicalDoc = "МРР-" + m.Groups["doc"].Value + "-" + m.Groups["yy"].Value,
            Year = year2 >= 80 ? 1900 + year2 : 2000 + year2,
            ParsePattern = "mrr"
        };
    }

    private static Basis? TryOrder(string text, string original)
    {
        var m = OrderCode().Match(text);
        if (!m.Success) return null;
        return new Basis
        {
            Kind = "ORDER",
            Raw = original,
            Doc = m.Groups[1].Value + "/pr",
            ParsePattern = "order"
        };
    }

    private static Basis? TryAsutp(string text, string original)
    {
        if (!AsutpAlias().IsMatch(text)) return null;
        var b = StateSbc(22, original, "asutp");
        var year = YearWord().Match(text);
        if (!year.Success) year = Regex.Match(text, @"(20\d{2})");
        if (year.Success && int.TryParse(year.Groups[1].Value, out var y)) b.Year = y;
        b.AdeptCode = "СБЦП 81-2001-22";
        return b;
    }

    private static Basis StateSbc(int nn, string original, string pattern) => new()
    {
        Kind = "SBC",
        Raw = original,
        Doc = $"СБЦП 81-2001-{nn}",
        CanonicalDoc = $"СБЦП 81-2001-{nn}",
        CollectionNum = nn,
        ParsePattern = pattern
    };

    private static void ApplySuffix(Basis b, Match m)
    {
        if (!m.Groups["part"].Success) return;
        b.Part = m.Groups["part"].Value;
        b.Chapter = m.Groups["chapter"].Value;
        b.Table = m.Groups["table"].Value;
        b.Item = m.Groups["item"].Value;
        if (b.ParsePattern is "long" or "short")
            b.ParsePattern = "adept-suffix";
    }

    private static string? CompactAdept(string text, Match m)
    {
        if (!m.Groups["part"].Success) return m.Value.Trim();
        return Normalize(m.Value);
    }

    private static void FillTableItem(Basis b, string text)
    {
        if (string.IsNullOrWhiteSpace(b.Table))
        {
            var table = TableWord().Match(text);
            if (table.Success) b.Table = AprXml.CleanRef(table.Groups[1].Value);
        }

        if (string.IsNullOrWhiteSpace(b.Item))
        {
            var item = ItemWord().Match(text);
            if (item.Success) b.Item = AprXml.CleanRef(item.Groups[1].Value);
        }
    }

    [GeneratedRegex(@"\bSBCP\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LatinSbcp();

    [GeneratedRegex(@"СБЦП\s*81\s*[-–]\s*02\s*[-–]\s*(?<nn>\d+)\s*[-–]\s*(?<year>\d{4})(?:\s*_(?<part>\d+)\s*-\s*(?<chapter>\d+)\s*-\s*(?<table>\d+)\s*-\s*(?<item>\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LongSbcp();

    [GeneratedRegex(@"СБЦП\s*81\s*[-–]\s*2001\s*[-–]\s*(?<nn>\d+)(?:\s*_(?<part>\d+)\s*-\s*(?<chapter>\d+)\s*-\s*(?<table>\d+)\s*-\s*(?<item>\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortSbcp();

    [GeneratedRegex(@"(?<head>СБЦП[^_\r\n]{0,80})_(?<part>\d+)\s*-\s*(?<chapter>\d+)\s*-\s*(?<table>\d+)\s*-\s*(?<item>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuffixOnly();

    [GeneratedRegex(@"СБЦП\s*(?<nn>\d{1,2})\s*-\s*(?<table>\d+)\s*-\s*(?<item>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GrandCompact();

    [GeneratedRegex(@"СБЦи\s*(?<ver>\d+(?:\.\d+)?)_(?<rest>\d+(?:-\d+)+)(?:\((?<n>\d+)\))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SurveyCode();

    [GeneratedRegex(@"НЗ\s*№?\s*(\d+)\s*/\s*пр", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NzDoc();

    [GeneratedRegex(@"МРР\s*[-–]\s*(?<doc>\d+(?:\.\d+)+)\s*[-–]\s*(?<yy>\d{2})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MrrCode();

    [GeneratedRegex(@"(?<![\d/])(\d+)\s*/\s*пр", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderCode();

    [GeneratedRegex(@"СБЦ\s+АСУТП", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsutpAlias();

    [GeneratedRegex(@"(20\d{2})\s*г", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex YearWord();

    [GeneratedRegex(@"табл(?:иц[аеы])?\.?\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TableWord();

    [GeneratedRegex(@"(?<![0-9A-Za-zА-Яа-я])(?:пункт|п)\.?\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemWord();
}
