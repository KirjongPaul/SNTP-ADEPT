using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Books;

public sealed class BookHit
{
    public string Status { get; set; } = "ok";
    public string Message { get; set; } = "";
    public string? EstimateId { get; set; }
    public string? PositionId { get; set; }
    public string? BookId { get; set; }
    public string? Table { get; set; }
    public string? Item { get; set; }
}

public static class BookReconciler
{
    /// <summary>Официальные реквизиты ФРСН для сборников, которых ещё нет в каталоге.</summary>
    public static readonly (string Token, int Year, string Date, string Title)[] Known =
    [
        ("849/пр", 2023, "28.11.2023", "НЗ на проектную документацию объектов газовой промышленности"),
        ("847/пр", 2023, "28.11.2023", "НЗ на проектную документацию сетей инженерно-технического обеспечения"),
        ("51/пр", 2021, "09.02.2021", "НЗ на проектные работы автомобильных дорог"),
        ("912/пр", 2023, "13.12.2023", "НЗ на проектную документацию инженерной защиты")
    ];

    public static IReadOnlyList<BookHit> Check(PirDocument doc, IReadOnlyList<ReferenceBook> books)
    {
        var hits = new List<BookHit>();
        foreach (var est in doc.Estimates)
        foreach (var pos in est.Positions.Where(p => p.Kind == PositionKind.Work))
        {
            var raw = pos.Basis?.Raw ?? "";
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var book = books.FirstOrDefault(b => Mentions(b, raw));
            if (book is null)
            {
                if (LooksLikeBook(raw))
                {
                    hits.Add(Hit("book-not-entered", "Сборник из обоснования ещё не введён.", est, pos));
                    RefMismatch(hits, est, pos, raw, null);
                }
                continue;
            }
            RefMismatch(hits, est, pos, raw, book);
            var tableCode = BookText.TableCode(raw);
            var table = book.Tables.FirstOrDefault(t => t.Kind == "price" && string.Equals(t.Code, tableCode, StringComparison.OrdinalIgnoreCase));
            if (table is null)
            {
                hits.Add(Hit("book-item-missing", $"В {book.OrderRef} нет таблицы {tableCode ?? "—"}.", est, pos, book.Id, tableCode));
                continue;
            }
            var itemNo = BookText.ItemNo(raw);
            var rows = table.Items.Where(i => BookText.NormItem(i.N) == BookText.NormItem(itemNo)).ToList();
            if (rows.Count == 0)
            {
                hits.Add(Hit("book-item-missing", $"В таблице {table.Code} нет пункта {itemNo}.", est, pos, book.Id, table.Code, itemNo));
                continue;
            }
            var row = pos.Quantity is decimal x
                ? rows.FirstOrDefault(i => BookText.ContainsX(i, x, rows)) ?? rows[0]
                : rows[0];
            if (pos.Quantity is decimal qx && rows.Any(i => i.Xmin is not null || i.Xmax is not null) && !rows.Any(i => BookText.ContainsX(i, qx, rows)))
                hits.Add(Hit("book-x-out-of-range", $"X={qx} вне диапазона пункта {itemNo} таблицы {table.Code}.", est, pos, book.Id, table.Code, itemNo));
            if (!BookText.SameMoney(row.A, pos.PriceA) || !BookText.SameMoney(row.B, pos.PriceB))
            {
                hits.Add(Hit("book-value-mismatch",
                    $"A/B строки не совпадают с {book.OrderRef} таблица {table.Code} п.{itemNo}: в сборнике а={row.A}, в={row.B} тыс. руб.",
                    est, pos, book.Id, table.Code, itemNo));
            }
        }
        return hits;
    }

    public static bool Mentions(ReferenceBook book, string raw)
    {
        foreach (var alias in book.Aliases)
        {
            if (alias.Length >= 3 && raw.Contains(alias, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool LooksLikeBook(string raw) =>
        raw.Contains("НЗ", StringComparison.OrdinalIgnoreCase)
        || raw.Contains("СБЦ", StringComparison.OrdinalIgnoreCase)
        || raw.Contains("/пр", StringComparison.OrdinalIgnoreCase)
        || raw.Contains("СБЦП", StringComparison.OrdinalIgnoreCase);

    private static void RefMismatch(List<BookHit> hits, Estimate est, Position pos, string raw, ReferenceBook? book)
    {
        var token = Known.FirstOrDefault(k => raw.Contains(k.Token, StringComparison.OrdinalIgnoreCase));
        if (token.Token is null && book?.OrderYear is int year)
        {
            var years = BookText.Years(raw).Select(int.Parse).ToList();
            if (years.Count > 0 && years.All(y => y != year))
                hits.Add(Hit("book-ref-mismatch", $"В обосновании год {string.Join(", ", years)}, в сборнике {year}.", est, pos, book.Id));
            return;
        }
        if (token.Token is null) return;
        var mentioned = BookText.Years(raw).Select(int.Parse).Distinct().ToList();
        var dateWrong = !string.IsNullOrEmpty(token.Date) && raw.Contains("от", StringComparison.OrdinalIgnoreCase)
            && System.Text.RegularExpressions.Regex.IsMatch(raw, @"\d{2}\.\d{2}\.\d{4}")
            && !raw.Contains(token.Date, StringComparison.Ordinal);
        if ((mentioned.Count > 0 && mentioned.All(y => y != token.Year)) || dateWrong)
            hits.Add(Hit("book-ref-mismatch",
                $"Реквизиты в обосновании расходятся с ФРСН: {token.Token} от {token.Date}.",
                est, pos, book?.Id));
    }

    private static BookHit Hit(string status, string message, Estimate est, Position pos, string? bookId = null, string? table = null, string? item = null) =>
        new()
        {
            Status = status,
            Message = message,
            EstimateId = est.Id,
            PositionId = pos.Id,
            BookId = bookId,
            Table = table,
            Item = item
        };
}
