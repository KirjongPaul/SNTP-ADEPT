namespace Sntp.Adept.Domain.Books;

public static class BookValidator
{
    public static IReadOnlyList<string> Check(ReferenceBook book)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(book.Id)) issues.Add("Нет идентификатора сборника.");
        if (string.IsNullOrWhiteSpace(book.Title)) issues.Add("Нет названия.");
        if (book.Codex is not null && CodexLinks.Href(book.Codex) is null)
            issues.Add("Номер документа Кодекса не распознан.");
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in book.Tables)
        {
            if (string.IsNullOrWhiteSpace(table.Code))
            {
                issues.Add("Таблица без кода.");
                continue;
            }
            if (!codes.Add(table.Code)) issues.Add($"Таблица {table.Code} повторяется.");
            if (!string.Equals(table.Kind, "price", StringComparison.OrdinalIgnoreCase)) continue;
            var seen = new List<BookItem>();
            foreach (var item in table.Items)
            {
                var where = $"{table.Code} п.{item.N}";
                if (string.IsNullOrWhiteSpace(item.N)) issues.Add($"{table.Code}: пункт без номера.");
                if (item.A is < 0) issues.Add($"{where}: параметр а отрицательный.");
                if (item.B is < 0) issues.Add($"{where}: параметр в отрицательный.");
                if (item.Xmin is decimal min && item.Xmax is decimal max && min > max)
                    issues.Add($"{where}: Xmin больше Xmax.");
                foreach (var other in seen)
                {
                    if (!string.Equals(BookText.NormItem(other.N), BookText.NormItem(item.N), StringComparison.Ordinal))
                        continue;
                    if (Overlaps(other, item))
                        issues.Add($"{where}: диапазоны X пересекаются.");
                }
                seen.Add(item);
            }
            foreach (var group in table.Items.GroupBy(i => BookText.NormItem(i.N) + "\n" + (i.Name ?? "").Trim()))
            {
                var bands = group
                    .Where(i => i.Xmin is not null || i.Xmax is not null)
                    .OrderBy(i => i.Xmin ?? decimal.MinValue)
                    .ThenBy(i => i.Xmax ?? decimal.MaxValue)
                    .ToList();
                for (var i = 1; i < bands.Count; i++)
                {
                    if (bands[i - 1].Xmax is not decimal end || bands[i].Xmin is not decimal start || start <= end)
                        continue;
                    if (bands[i - 1].Xmin == end && bands[i].Xmax == start)
                        continue;
                    issues.Add($"{table.Code} п.{bands[i].N}: дыра между {end} и {start}.");
                }
            }
        }
        foreach (var note in book.Notes)
        {
            if (string.IsNullOrWhiteSpace(note.Anchor))
                issues.Add($"Разъяснение «{note.LetterRef}» без якоря.");
            else if (!note.Anchor.StartsWith("general:", StringComparison.Ordinal)
                     && !note.Anchor.StartsWith("table:", StringComparison.Ordinal)
                     && !note.Anchor.StartsWith("item:", StringComparison.Ordinal)
                     && !note.Anchor.StartsWith("rule:", StringComparison.Ordinal))
                issues.Add($"Якорь «{note.Anchor}» не распознан.");
        }
        return issues;
    }

    private static bool Overlaps(BookItem a, BookItem b)
    {
        if (a.Xmin is null && a.Xmax is null) return false;
        if (b.Xmin is null && b.Xmax is null) return false;
        var a0 = a.Xmin ?? decimal.MinValue;
        var a1 = a.Xmax ?? decimal.MaxValue;
        var b0 = b.Xmin ?? decimal.MinValue;
        var b1 = b.Xmax ?? decimal.MaxValue;
        return a0 < b1 && b0 < a1 && !(a1 == b0 || b1 == a0);
    }
}
