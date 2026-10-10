using System.Text.RegularExpressions;

namespace Sntp.Adept.Domain.Books;

/// <summary>Разбор текста приказа НЗ, уже разложенного на абзацы и таблицы Markdown.</summary>
public static class NzMarkdown
{
    public static ReferenceBook Parse(string text, string sourceFile)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var book = Passport(text, sourceFile);
        book.GeneralPart = GeneralClauses(lines, sourceFile);
        AddRules(book);
        ParseTables(lines, book, sourceFile);
        return book;
    }

    private static ReferenceBook Passport(string text, string sourceFile)
    {
        var num = Regex.Match(text, @"N\s*(\d+)/пр");
        var year = Regex.Match(text, @"(20\d{2})\s*г\.\s*N");
        var orderNum = num.Success ? num.Groups[1].Value : "0";
        var orderYear = year.Success ? int.Parse(year.Groups[1].Value) : (int?)null;
        return new ReferenceBook
        {
            Id = orderYear is int y ? $"nz-{orderNum}-{y}" : "nz-" + orderNum,
            Kind = "NZ",
            Title = "Нормативные затраты на проектную документацию для строительства объектов газовой промышленности",
            OrderRef = $"№{orderNum}/пр" + (orderYear is int oy ? $" от 28.11.{oy}" : ""),
            OrderDate = orderYear is int od ? $"28.11.{od}" : null,
            OrderYear = orderYear,
            PriceLevel = "01.01.2022",
            FrsnName = "Нормативные затраты на работы по подготовке проектной документации для строительства объектов газовой промышленности",
            FrsnMatch = orderNum == "849" && orderYear == 2023,
            Status = "draft",
            Aliases = [$"{orderNum}/пр", $"№{orderNum}", $"НЗ №{orderNum}", "объекты газовой промышленности"],
            Folder = sourceFile
        };
    }

    private static List<BookClause> GeneralClauses(string[] lines, string sourceFile)
    {
        var list = new List<BookClause>();
        var start = IndexOf(lines, s => s.StartsWith("I. Общие положения", StringComparison.Ordinal));
        var end = IndexOf(lines, s => s.StartsWith("III.", StringComparison.Ordinal));
        if (start < 0) start = 0;
        if (end < 0) end = lines.Length;
        BookClause? current = null;
        for (var i = start; i < end; i++)
        {
            var line = lines[i].Trim();
            var m = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
            if (m.Success && int.Parse(m.Groups[1].Value) <= 23)
            {
                current = new BookClause
                {
                    Id = m.Groups[1].Value,
                    Text = m.Groups[2].Value.Trim(),
                    Src = new BookSource { File = sourceFile, Page = i + 1, Quote = Trim(line) }
                };
                list.Add(current);
            }
            else if (current is not null && line.Length > 0 && !line.StartsWith("Таблица", StringComparison.Ordinal) && !line.StartsWith('|'))
                current.Text = (current.Text + " " + line).Trim();
        }
        return list;
    }

    private static void AddRules(ReferenceBook book)
    {
        var clause = book.GeneralPart.FirstOrDefault(c => c.Id == "13");
        void Add(string kind, string text) => book.Rules.Add(new BookRule
        {
            Id = kind,
            Kind = kind,
            ClauseId = "13",
            Text = text,
            Src = clause?.Src
        });
        Add("range-select", "Показатель внутри интервала пункта: (A+B·X)·K, формула (8.1) Методики 707/пр, на которую ссылается пункт 13.");
        Add("interpolate-below-min", "Показатель ниже Xmin, но не меньше 0,5·Xmin: (A+B·(0,4·Xmin+0,6·X))·K, формулы (8.2)–(8.8) Методики 707/пр.");
        Add("extrapolate-above-max", "Показатель выше Xmax: (A+B·(0,4·Xmax+0,6·X))·K, формулы (8.2)–(8.8) Методики 707/пр.");
        Add("kpon-below-half", "Показатель ниже 0,5·Xmin: к формуле ниже минимума добавляется Кпон = X/(0,5·Xmin), Методика 707/пр.");
        var similar = book.GeneralPart.FirstOrDefault(c => c.Id == "14");
        if (similar is not null)
            book.Rules.Add(new BookRule
            {
                Id = "factor-when-similar",
                Kind = "factor-when",
                ClauseId = "14",
                Text = "Каждый следующий аналогичный объект в составе одного строительства — коэффициент 0,5. На линейную часть и кусты скважин не распространяется.",
                Src = similar.Src
            });
    }

    private static void ParseTables(string[] lines, ReferenceBook book, string sourceFile)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var head = Regex.Match(lines[i].Trim(), @"^Таблица\s+(\d+(?:\.\d+)*)\s*$");
            if (!head.Success) continue;
            var code = head.Groups[1].Value;
            var row = i + 1;
            while (row < lines.Length && !lines[row].TrimStart().StartsWith('|')) row++;
            if (row >= lines.Length) continue;
            var block = new List<(int Line, string[] Cells)>();
            while (row < lines.Length && lines[row].TrimStart().StartsWith('|'))
            {
                block.Add((row + 1, Cells(lines[row])));
                row++;
            }
            if (block.Count == 0) continue;
            var header = string.Join(" ", block.Take(3).SelectMany(b => b.Cells));
            var kind = header.Contains("\"а\"", StringComparison.Ordinal) || header.Contains("Параметры цены", StringComparison.Ordinal)
                ? "price"
                : header.Contains("коэффициент", StringComparison.OrdinalIgnoreCase) ? "factor" : "share";
            var table = new BookTable
            {
                Code = code,
                Kind = kind,
                Name = TitleBefore(lines, i),
                Src = new BookSource { File = sourceFile, Page = i + 1, Quote = "Таблица " + code }
            };
            string carryN = "";
            string carryName = "";
            foreach (var (lineNo, cells) in block)
            {
                if (cells.Length < 2 || IsChrome(cells)) continue;
                if (kind == "price")
                {
                    if (cells.Length < 5) continue;
                    var n = cells[0];
                    var name = cells[1];
                    if (n.Length > 0) carryN = n.Trim().TrimEnd('.');
                    if (name.Length > 0) carryName = name;
                    var a = BookText.ParseDecimal(cells[4]);
                    var b = cells.Length > 5 ? BookText.ParseDecimal(cells[5]) : null;
                    if (a is null && b is null) continue;
                    Bounds(cells[3], out var xmin, out var xmax);
                    var unit = cells[2];
                    table.Items.Add(new BookItem
                    {
                        N = carryN,
                        Name = carryName,
                        Unit = unit,
                        Xmin = xmin,
                        Xmax = xmax,
                        A = a,
                        B = b,
                        Check = "draft",
                        Src = new BookSource { File = sourceFile, Page = lineNo, Quote = Trim(string.Join(" | ", cells)) }
                    });
                    if (!string.IsNullOrWhiteSpace(unit)) table.ParamUnit ??= unit;
                }
                else if (kind == "factor")
                {
                    if (cells.Length < 3) continue;
                    var n = cells[0].Trim().TrimEnd('.');
                    var value = BookText.ParseDecimal(cells[2]);
                    if (n.Length == 0 || value is null) continue;
                    var factor = new BookFactor
                    {
                        Id = code + "/" + n,
                        Name = cells[1],
                        Value = value,
                        Condition = cells.Length > 3 ? cells[3] : null,
                        ClauseId = code,
                        Src = new BookSource { File = sourceFile, Page = lineNo, Quote = Trim(cells[1]) }
                    };
                    book.Factors.Add(factor);
                    table.Items.Add(new BookItem { N = n, Name = cells[1], A = value, Unit = "коэф.", Check = "draft", Src = factor.Src });
                }
                else
                {
                    var n = cells[0].Trim().TrimEnd('.');
                    if (n.Length == 0 || n.StartsWith("Таблица", StringComparison.Ordinal)) continue;
                    table.Items.Add(new BookItem
                    {
                        N = n,
                        Name = string.Join("; ", cells.Skip(1).Where(c => c.Length > 0)),
                        Check = "draft",
                        Src = new BookSource { File = sourceFile, Page = lineNo }
                    });
                }
            }
            if (table.Items.Count > 0) book.Tables.Add(table);
            if (kind == "price") Footnotes(lines, row, code, book, sourceFile);
        }
    }

    private static void Footnotes(string[] lines, int from, string code, ReferenceBook book, string sourceFile)
    {
        var n = 0;
        var text = "";
        var page = from + 1;
        void Flush()
        {
            if (n == 0 || text.Length == 0) return;
            foreach (var anchor in Anchors(text, code))
            {
                book.Notes.Add(new BookNote
                {
                    Id = $"{code}-{n}-{anchor}",
                    LetterRef = $"НЗ {book.OrderRef}, примечание {n} к таблице {code}",
                    LetterDate = book.OrderDate,
                    Anchor = anchor,
                    Quote = text.Trim(),
                    Page = page,
                    File = sourceFile
                });
            }
        }
        for (var i = from; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Таблица ", StringComparison.Ordinal) || line.StartsWith('|')) break;
            if (line.Equals("Примечание:", StringComparison.OrdinalIgnoreCase)) continue;
            var m = Regex.Match(line, @"^(\d+)\.\s+(.*)$");
            if (m.Success)
            {
                var num = int.Parse(m.Groups[1].Value);
                if (num > 20) break;
                Flush();
                n = num;
                text = m.Groups[2].Value.Trim();
                page = i + 1;
            }
            else if (n > 0)
                text = text + "\n" + line.Trim();
            else break;
        }
        Flush();
    }

    private static IEnumerable<string> Anchors(string text, string code)
    {
        var found = new List<string>();
        foreach (Match m in Regex.Matches(text, @"пункт\w*\s+(\d+(?:\.\d+)?)(?:\s*[-–—]\s*(\d+))?", RegexOptions.IgnoreCase))
        {
            if (m.Groups[2].Success && int.TryParse(m.Groups[1].Value, out var a) && int.TryParse(m.Groups[2].Value, out var b))
            {
                for (var i = a; i <= b; i++) found.Add(BookText.AnchorItem(code, i.ToString()));
            }
            else found.Add(BookText.AnchorItem(code, m.Groups[1].Value));
        }
        if (found.Count == 0) found.Add(BookText.AnchorTable(code));
        return found.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void Bounds(string text, out decimal? xmin, out decimal? xmax)
    {
        xmin = xmax = null;
        var range = Regex.Match(text, @"от\s+([\d\s,]+?)\s+до\s+([\d\s,]+)", RegexOptions.IgnoreCase);
        if (range.Success)
        {
            xmin = BookText.ParseDecimal(range.Groups[1].Value);
            xmax = BookText.ParseDecimal(range.Groups[2].Value);
            return;
        }
        var below = Regex.Match(text, @"до\s+([\d\s,]+)", RegexOptions.IgnoreCase);
        if (below.Success) xmax = BookText.ParseDecimal(below.Groups[1].Value);
        var above = Regex.Match(text, @"свыше\s+([\d\s,]+)", RegexOptions.IgnoreCase);
        if (above.Success)
        {
            xmin = BookText.ParseDecimal(above.Groups[1].Value);
            return;
        }
        var point = BookText.ParseDecimal(text);
        if (point is not null && Regex.IsMatch(text.Trim(), @"^[\d\s,]+$"))
            xmin = xmax = point;
    }

    private static bool IsChrome(string[] cells)
    {
        var joined = string.Join(" ", cells);
        if (joined.Contains("Наименование", StringComparison.Ordinal)) return true;
        if (cells[0] is "N" or "N п/п" or "1") return cells.Length > 1 && cells[1] is "2" or "Наименование объекта";
        return cells[0] == "1" && cells.Contains("2");
    }

    private static string TitleBefore(string[] lines, int tableLine)
    {
        for (var i = tableLine - 1; i >= 0 && i > tableLine - 6; i--)
        {
            var line = lines[i].Trim();
            if (line.Length > 20 && !line.StartsWith('|')) return Trim(line);
        }
        return "";
    }

    private static string[] Cells(string line) =>
        line.Trim().Trim('|').Split('|').Select(s => s.Trim()).ToArray();

    private static int IndexOf(string[] lines, Func<string, bool> test)
    {
        for (var i = 0; i < lines.Length; i++)
            if (test(lines[i].Trim())) return i;
        return -1;
    }

    private static string Trim(string text) => text.Length <= 240 ? text : text[..240];
}
