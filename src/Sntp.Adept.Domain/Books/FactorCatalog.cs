using System.Text.RegularExpressions;

namespace Sntp.Adept.Domain.Books;

/// <summary>
/// Общая переменная усложняющего фактора. Одно системное имя, русское имя для человека,
/// и список сборников, где она встречается.
/// </summary>
public sealed class FactorVariable
{
    public string Id { get; set; } = "";
    public string Alias { get; set; } = "";
    public string Name { get; set; } = "";
    public List<FactorUse> Uses { get; set; } = [];
}

/// <summary>Где переменная действует: сборник, таблица коэффициента и таблицы цены.</summary>
public sealed class FactorUse
{
    public string BookId { get; set; } = "";
    public string BookTitle { get; set; } = "";
    /// <summary>Короткий номер сборника для сверки с основанием строки, например 849.</summary>
    public string BookMark { get; set; } = "";
    public string Table { get; set; } = "";
    public string Item { get; set; } = "";
    public decimal Value { get; set; }
    public List<string> PriceTables { get; set; } = [];
}

public static class FactorCatalog
{
    private static readonly (string Id, string Alias, string Needle)[] Known =
    [
        ("historicZone", "Историческая среда", "историческ"),
        ("hydrogenSulfide", "Сероводород", "сероводород"),
        ("tightSite", "Стеснённые условия", "стеснен"),
        ("pipeInsulation", "Теплоизоляция", "теплоизоляц")
    ];

    /// <summary>Коэффициент одной таблицы цены (3.1.1), а не общая переменная.</summary>
    public static bool IsTableCoefficient(string code) =>
        Regex.IsMatch(code, @"^\d+\.\d+\.\d+$");

    public static List<FactorVariable> Build(IEnumerable<ReferenceBook> books)
    {
        var map = new Dictionary<string, FactorVariable>(StringComparer.Ordinal);
        foreach (var book in books)
        {
            var prices = book.Tables.Where(t => t.Kind == "price").Select(t => t.Code).ToList();
            foreach (var table in book.Tables.Where(t => t.Kind == "factor" && !IsTableCoefficient(t.Code)))
            {
                foreach (var item in table.Items)
                {
                    var known = Match(item.Name);
                    if (known is null) continue;
                    if (!map.TryGetValue(known.Value.Id, out var variable))
                    {
                        variable = new FactorVariable
                        {
                            Id = known.Value.Id,
                            Alias = known.Value.Alias,
                            Name = item.Name
                        };
                        map[known.Value.Id] = variable;
                    }
                    if (variable.Uses.Any(u => u.BookId == book.Id && u.Table == table.Code && u.Item == item.N))
                        continue;
                    variable.Uses.Add(new FactorUse
                    {
                        BookId = book.Id,
                        BookTitle = string.IsNullOrWhiteSpace(book.Title) ? book.Id : book.Title,
                        BookMark = BookMark(book),
                        Table = table.Code,
                        Item = item.N,
                        Value = item.A ?? 0m,
                        PriceTables = prices
                    });
                }
            }
        }
        return map.Values.OrderBy(v => v.Alias, StringComparer.CurrentCulture).ToList();
    }

    public static List<FactorVariable> ForBook(IEnumerable<FactorVariable> all, string bookId) =>
        all.Where(v => v.Uses.Any(u => string.Equals(u.BookId, bookId, StringComparison.OrdinalIgnoreCase))).ToList();

    private static (string Id, string Alias)? Match(string name)
    {
        foreach (var known in Known)
        {
            if (name.Contains(known.Needle, StringComparison.OrdinalIgnoreCase))
                return (known.Id, known.Alias);
        }
        return null;
    }

    private static string BookMark(ReferenceBook book)
    {
        var mark = Regex.Match(book.Id, @"\d{2,}");
        return mark.Success ? mark.Value : book.Id;
    }
}
