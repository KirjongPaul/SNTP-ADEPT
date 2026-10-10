using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

public static class AnalogMatcher
{
    public static void Apply(IEnumerable<Position> positions, AnalogMappings maps)
    {
        foreach (var pos in positions)
        foreach (var factor in pos.Factors)
        {
            var rule = maps.Rules.FirstOrDefault(r => Same(r.From, factor.Identifier) || Same(r.From, factor.Caption));
            if (rule is not null && rule.To.Length > 0)
            {
                factor.Identifier = rule.To;
                if (rule.Role.Length > 0) factor.Role = rule.Role;
                continue;
            }
            var text = factor.Identifier + " " + factor.Caption;
            if (text.Contains("стесн", StringComparison.OrdinalIgnoreCase))
            {
                factor.Role = "constraint";
                continue;
            }
            if (text.Contains("стади", StringComparison.OrdinalIgnoreCase) || text.Contains("Ки1", StringComparison.OrdinalIgnoreCase))
                factor.Role = "stage";
        }
    }

    public static List<AnalogRowScore> Score(IEnumerable<Estimate> estimates, IReadOnlyList<ReferenceBook> books)
    {
        var scores = new List<AnalogRowScore>();
        foreach (var est in estimates)
        foreach (var pos in est.Positions.Where(p => p.Kind == PositionKind.Work))
            scores.Add(ScoreOne(est, pos, books));
        return scores;
    }

    public static AnalogRowScore ScoreOne(Estimate est, Position pos, IReadOnlyList<ReferenceBook> books)
    {
        var raw = pos.Basis?.Raw ?? "";
        var score = new AnalogRowScore
        {
            EstimateId = est.Id,
            PositionId = pos.Id,
            LooksLikeBook = BookReconciler.LooksLikeBook(raw)
        };
        if (!score.LooksLikeBook)
        {
            score.Confidence = 1;
            score.Note = "Без ссылки на сборник";
            return score;
        }
        var book = books.FirstOrDefault(b => BookReconciler.Mentions(b, raw));
        if (book is null)
        {
            score.Confidence = 0;
            score.Note = "Сборник не разобран";
            return score;
        }
        var tableCode = pos.Basis?.Table ?? BookText.TableCode(raw);
        var itemNo = pos.Basis?.Item ?? BookText.ItemNo(raw);
        var table = book.Tables.FirstOrDefault(t => t.Kind == "price" && string.Equals(t.Code, tableCode, StringComparison.OrdinalIgnoreCase));
        var rows = table?.Items.Where(i => BookText.NormItem(i.N) == BookText.NormItem(itemNo)).ToList() ?? [];
        if (table is null || rows.Count == 0)
        {
            score.Confidence = NameScore(book, pos.Name);
            score.Note = score.Confidence > 0 ? $"Похоже на пункт сборника {book.OrderRef}" : $"В {book.OrderRef} нет таблицы {tableCode ?? "—"} п.{itemNo ?? "—"}";
            return score;
        }
        var band = Pick(rows, pos.Quantity);
        var money = band is not null && BookText.SameMoney(band.A, pos.PriceA) && (band.B is null || BookText.SameMoney(band.B, pos.PriceB));
        var inside = pos.Quantity is not decimal x || BookText.ContainsX(band!, x, rows) || Lump(band!, x);
        score.Confidence = money && inside ? 1 : 0.9;
        score.Note = money && inside
            ? $"{book.OrderRef} табл. {table.Code} п.{itemNo}"
            : $"{book.OrderRef} табл. {table.Code} п.{itemNo}, полоса или A/B разошлись";
        return score;
    }

    public static bool NeedsTune(IEnumerable<AnalogRowScore> scores) =>
        scores.Any(s => s.LooksLikeBook && s.Confidence < 0.6);

    private static BookItem? Pick(List<BookItem> rows, decimal? quantity)
    {
        if (quantity is not decimal x) return rows[0];
        var hit = rows.FirstOrDefault(i => BookText.ContainsX(i, x, rows));
        if (hit is not null) return hit;
        return rows.FirstOrDefault(i => Lump(i, x)) ?? rows[0];
    }

    private static bool Lump(BookItem item, decimal x)
    {
        if (item.B is not null) return false;
        if (item.Unit is null || !item.Unit.Contains("объект", StringComparison.OrdinalIgnoreCase)) return false;
        if (item.Xmax is not decimal max) return false;
        return item.Xmin is null && x <= max;
    }

    private static double NameScore(ReferenceBook book, string name)
    {
        var words = Tokens(name);
        if (words.Count == 0) return 0;
        double best = 0;
        foreach (var item in book.Tables.Where(t => t.Kind == "price").SelectMany(t => t.Items))
        {
            var other = Tokens(item.Name);
            if (other.Count == 0) continue;
            var shared = words.Intersect(other).Count();
            var score = shared / (double)Math.Max(words.Count, other.Count);
            if (score > best) best = score;
        }
        return Math.Min(0.74, best);
    }

    private static HashSet<string> Tokens(string text) =>
        text.Split([' ', ',', '.', ';', ':', '(', ')', '«', '»', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length > 3)
            .ToHashSet();

    private static bool Same(string a, string b) =>
        string.Equals(Fold(a), Fold(b), StringComparison.OrdinalIgnoreCase);

    private static string Fold(string value) =>
        value.Replace(" ", "").Replace("К", "K", StringComparison.Ordinal).Replace("к", "k", StringComparison.Ordinal);
}
