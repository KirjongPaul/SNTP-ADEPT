using System.Globalization;
using System.Text.RegularExpressions;

namespace Sntp.Adept.Domain.Books;

public static class BookText
{
    public static decimal? ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Replace(" ", "").Replace("\u00a0", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static string NormItem(string? n)
    {
        var s = (n ?? "").Trim().TrimEnd('.');
        return s;
    }

    public static bool SameNumber(decimal? a, decimal? b, decimal tol = 0.0005m)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) <= tol;
    }

    /// <summary>Цена сборника в тыс. руб. совпадает с ценой строки в рублях или уже в тысячах.</summary>
    public static bool SameMoney(decimal? bookThousands, decimal? position)
    {
        if (bookThousands is null && position is null) return true;
        if (bookThousands is null || position is null) return false;
        var rub = bookThousands.Value * 1000m;
        if (Math.Abs(position.Value - rub) <= 1.5m) return true;
        return Math.Abs(position.Value - bookThousands.Value) <= 0.05m;
    }

    public static decimal ToRubles(decimal thousands) => Math.Round(thousands * 1000m, 3);

    public static bool ContainsX(BookItem item, decimal x, IReadOnlyList<BookItem>? siblings = null)
    {
        if (item.Xmin is null && item.Xmax is null) return false;
        if (item.Xmin is decimal min && x < min) return false;
        if (item.Xmax is decimal max && x > max) return false;
        if (item.Xmin is decimal edge && x == edge && siblings is not null
            && siblings.Any(s => !ReferenceEquals(s, item) && s.Xmax == edge))
            return false;
        return true;
    }

    public static string AnchorItem(string table, string item) => $"item:{table}/{NormItem(item)}";
    public static string AnchorTable(string table) => $"table:{table}";

    public static IEnumerable<string> Years(string text) =>
        Regex.Matches(text, @"20\d{2}").Select(m => m.Value).Distinct();

    public static string? TableCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = Regex.Match(raw, @"[Тт]абл(?:ица|\.)\s*(\d+(?:\.\d+)*)");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string? ItemNo(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var m = Regex.Match(raw, @"п(?:ункт|\.)\s*(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }
}
