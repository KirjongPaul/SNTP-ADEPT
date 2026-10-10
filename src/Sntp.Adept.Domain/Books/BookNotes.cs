namespace Sntp.Adept.Domain.Books;

public static class BookNotes
{
    public static IReadOnlyList<BookNote> Lookup(ReferenceBook book, string? anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor)) return book.Notes;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { anchor };
        if (anchor.StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            var body = anchor["item:".Length..];
            var slash = body.IndexOf('/');
            if (slash > 0) keys.Add("table:" + body[..slash]);
        }
        return book.Notes.Where(n => keys.Contains(n.Anchor)).ToList();
    }
}
