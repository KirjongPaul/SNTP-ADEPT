namespace Sntp.Adept.Domain.Books;

/// <summary>Сборник НЗ или СБЦ. Значения цены — в тысячах рублей, как в приказе.</summary>
public sealed class ReferenceBook
{
    public string Schema { get; set; } = "adept-book.v1";
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "NZ";
    public string Title { get; set; } = "";
    public string OrderRef { get; set; } = "";
    public string? OrderDate { get; set; }
    public int? OrderYear { get; set; }
    public string? PriceLevel { get; set; }
    public string? FrsnName { get; set; }
    public bool? FrsnMatch { get; set; }
    public string Status { get; set; } = "draft";
    /// <summary>Оригинал в интранете. Копия для расчёта лежит отдельно, в source/.</summary>
    public CodexRef? Codex { get; set; }
    public string? Folder { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<BookClause> GeneralPart { get; set; } = [];
    public List<BookTable> Tables { get; set; } = [];
    public List<BookRule> Rules { get; set; } = [];
    public List<BookFactor> Factors { get; set; } = [];
    public List<BookNote> Notes { get; set; } = [];
}

/// <summary>Документ Кодекса, с которым сверяли сборник. В json хранится только nd.</summary>
public sealed class CodexRef
{
    public string Nd { get; set; } = "";
}

public static class CodexLinks
{
    /// <summary>Прямая ссылка, которую Кодекс копирует сам: /docs/d?nd=.</summary>
    public static string? Href(CodexRef? codex)
    {
        var nd = codex?.Nd.Trim();
        if (string.IsNullOrEmpty(nd) || nd.Any(c => !char.IsDigit(c))) return null;
        return "http://techexpert/docs/d?nd=" + nd;
    }
}

public sealed class BookSource
{
    public string? File { get; set; }
    public int? Page { get; set; }
    public string? Quote { get; set; }
}

public sealed class BookClause
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public BookSource? Src { get; set; }
}

public sealed class BookTable
{
    public string Code { get; set; } = "";
    /// <summary>price — параметры а/в; factor — коэффициенты; share — доли разделов.</summary>
    public string Kind { get; set; } = "price";
    public string Name { get; set; } = "";
    public string? ParamUnit { get; set; }
    public List<BookItem> Items { get; set; } = [];
    public BookSource? Src { get; set; }
}

public sealed class BookItem
{
    public string N { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? Xmin { get; set; }
    public decimal? Xmax { get; set; }
    public decimal? A { get; set; }
    public decimal? B { get; set; }
    public BookSource? Src { get; set; }
    /// <summary>agreed, resolved, disputed, draft.</summary>
    public string Check { get; set; } = "draft";
}

public sealed class BookRule
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? ClauseId { get; set; }
    public string? Text { get; set; }
    public BookSource? Src { get; set; }
}

public sealed class BookFactor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal? Value { get; set; }
    public string? Condition { get; set; }
    public string? ClauseId { get; set; }
    public BookSource? Src { get; set; }
}

public sealed class BookNote
{
    public string Id { get; set; } = "";
    public string LetterRef { get; set; } = "";
    public string? LetterDate { get; set; }
    public string Anchor { get; set; } = "";
    public string Quote { get; set; } = "";
    public int? Page { get; set; }
    public string? File { get; set; }
    public string? Url { get; set; }
}

public sealed class WorkVolume
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public List<string> PositionIds { get; set; } = [];
    public string? BookId { get; set; }
    public string? Table { get; set; }
    public string? Item { get; set; }
    public string Status { get; set; } = "bound";
    public List<VolumeCandidate> Candidates { get; set; } = [];
    public List<string> ChosenItems { get; set; } = [];
    public List<string> Protocol { get; set; } = [];
    /// <summary>Принятая полоса. Пусто, если объём попал в дыру или полосы пересекаются.</summary>
    public VolumePick? Pick { get; set; }
}

public sealed class VolumePick
{
    public string Item { get; set; } = "";
    public decimal? Xmin { get; set; }
    public decimal? Xmax { get; set; }
    public string? Unit { get; set; }
    public decimal? A { get; set; }
    public decimal? B { get; set; }
    /// <summary>within, below, below-half, above, lump.</summary>
    public string Kind { get; set; } = "";
}

public sealed class VolumeCandidate
{
    public string BookId { get; set; } = "";
    public string Table { get; set; } = "";
    public string Item { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    public decimal? Xmin { get; set; }
    public decimal? Xmax { get; set; }
    public decimal? A { get; set; }
    public decimal? B { get; set; }
    public string Reason { get; set; } = "";
}
