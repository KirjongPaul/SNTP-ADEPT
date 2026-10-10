namespace Sntp.Adept.Domain.Books;

public sealed record BookFieldDiff(string Path, string? Draft, string? Check);

public static class BookDiff
{
    public static IReadOnlyList<BookFieldDiff> Compare(ReferenceBook draft, ReferenceBook check)
    {
        if (string.Equals(check.Status, "check", StringComparison.OrdinalIgnoreCase))
        {
            var codes = check.Tables.Select(t => t.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            draft = Slice(draft, codes);
            check = Slice(check, codes);
        }
        var list = new List<BookFieldDiff>();
        Field(list, "title", draft.Title, check.Title);
        Field(list, "orderRef", draft.OrderRef, check.OrderRef);
        Field(list, "orderYear", draft.OrderYear?.ToString(), check.OrderYear?.ToString());

        var draftTables = draft.Tables.Where(t => t.Kind == "price").ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        var checkTables = check.Tables.Where(t => t.Kind == "price").ToDictionary(t => t.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var code in draftTables.Keys.Union(checkTables.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            draftTables.TryGetValue(code, out var left);
            checkTables.TryGetValue(code, out var right);
            if (left is null || right is null)
            {
                list.Add(new BookFieldDiff($"table:{code}", left is null ? null : "есть", right is null ? null : "есть"));
                continue;
            }
            var lItems = left.Items.ToDictionary(Key);
            var rItems = right.Items.ToDictionary(Key);
            foreach (var key in lItems.Keys.Union(rItems.Keys).Order(StringComparer.Ordinal))
            {
                lItems.TryGetValue(key, out var a);
                rItems.TryGetValue(key, out var b);
                var path = $"table:{code}/item:{key}";
                if (a is null || b is null)
                {
                    list.Add(new BookFieldDiff(path, a is null ? null : Money(a), b is null ? null : Money(b)));
                    continue;
                }
                Field(list, path + "/a", Fmt(a.A), Fmt(b.A));
                Field(list, path + "/b", Fmt(a.B), Fmt(b.B));
                Field(list, path + "/xmin", Fmt(a.Xmin), Fmt(b.Xmin));
                Field(list, path + "/xmax", Fmt(a.Xmax), Fmt(b.Xmax));
            }
        }

        var lNotes = draft.Notes.Select(NoteKey).Order(StringComparer.Ordinal).ToList();
        var rNotes = check.Notes.Select(NoteKey).Order(StringComparer.Ordinal).ToList();
        foreach (var key in lNotes.Except(rNotes).Concat(rNotes.Except(lNotes)))
        {
            var side = lNotes.Contains(key) ? "draft" : null;
            list.Add(new BookFieldDiff("note:" + key, side, side is null ? "check" : null));
        }
        return list;
    }

    private static ReferenceBook Slice(ReferenceBook book, HashSet<string> codes) => new()
    {
        Title = book.Title,
        OrderRef = book.OrderRef,
        OrderYear = book.OrderYear,
        Tables = book.Tables.Where(t => t.Kind == "price" && codes.Contains(t.Code)).ToList(),
        Notes = book.Notes.Where(n => codes.Any(c => n.Anchor.Contains(c, StringComparison.OrdinalIgnoreCase))).ToList()
    };

    private static string Key(BookItem item) =>
        $"{BookText.NormItem(item.N)}@{Fmt(item.Xmin)}-{Fmt(item.Xmax)}";

    private static string NoteKey(BookNote note) =>
        $"{note.Anchor}|{note.LetterRef}|{note.Quote.Trim()}";

    private static string Money(BookItem item) => $"a={Fmt(item.A)};b={Fmt(item.B)}";

    private static string Fmt(decimal? v) =>
        v is null ? "" : v.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static void Field(List<BookFieldDiff> list, string path, string? draft, string? check)
    {
        if (!string.Equals(draft ?? "", check ?? "", StringComparison.Ordinal))
            list.Add(new BookFieldDiff(path, draft, check));
    }
}
