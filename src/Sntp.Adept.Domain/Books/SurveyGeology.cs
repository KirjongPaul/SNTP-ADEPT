using System.Globalization;

namespace Sntp.Adept.Domain.Books;

/// <summary>
/// Справочник параметров инженерно-геологических изысканий линейного газопровода.
/// Не наследник <see cref="BookTableGroup"/>: группа таблицы подбирает полосу a/b,
/// а здесь натуральный показатель считается по пункту СП и только потом указывает строку НЗ.
/// Цифры шага и глубины — из карточки СП 446.1325800.2019, не из эскиза.
/// </summary>
public static class SurveyGeology
{
    public const string Kind = "geology-linear-gas";
    public const string SpNd = "561027906";
    public const string NzNd = "1312801877";
    public const string NzBookId = "nz-281-2025";
    public const string GasRow = "Водопровод, канализация, теплосеть и газопровод";

    public const decimal SpacingMinM = 100m;
    public const decimal SpacingMaxM = 300m;
    public const decimal WidthMinM = 100m;
    public const decimal WidthMaxM = 200m;

    private static readonly string[] Categories = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];

    /// <summary>Таблица 14 п. 55: полосы диаметра и конечной глубины, номер пункта без цены.</summary>
    private static readonly DrillBand[] Bands =
    [
        new(0, 160, 0, 15, "I", "1"), new(0, 160, 0, 15, "II", "2"), new(0, 160, 0, 15, "III", "3"),
        new(0, 160, 0, 15, "IV", "4"), new(0, 160, 0, 15, "V", "5"), new(0, 160, 0, 15, "VI", "6"),
        new(0, 160, 0, 15, "VII", "7"), new(0, 160, 0, 15, "VIII", "8"),
        new(0, 160, 15, 25, "I", "9"), new(0, 160, 15, 25, "II", "10"), new(0, 160, 15, 25, "III", "11"),
        new(0, 160, 15, 25, "IV", "12"), new(0, 160, 15, 25, "V", "13"), new(0, 160, 15, 25, "VI", "14"),
        new(0, 160, 15, 25, "VII", "15"), new(0, 160, 15, 25, "VIII", "16"),
        new(0, 160, 25, 50, "I", "17"), new(0, 160, 25, 50, "II", "18"), new(0, 160, 25, 50, "III", "19"),
        new(0, 160, 25, 50, "IV", "20"), new(0, 160, 25, 50, "V", "21"), new(0, 160, 25, 50, "VI", "22"),
        new(0, 160, 25, 50, "VII", "23"), new(0, 160, 25, 50, "VIII", "24"),
        new(0, 160, 50, 100, "II", "25"), new(0, 160, 50, 100, "III", "26"), new(0, 160, 50, 100, "IV", "27"),
        new(0, 160, 50, 100, "V", "28"), new(0, 160, 50, 100, "VI", "29"),
        new(0, 76, 0, 25, "IX", "30"),
        new(0, 76, 25, 100, "IX", "31"),
        new(76, 122, 0, 25, "IX", "32"),
        new(76, 122, 25, 100, "IX", "33")
    ];

    public static SurveySheet Compose(SurveyInputs input, IReadOnlyList<SurveyParameter>? previous = null)
    {
        var manuals = (previous ?? [])
            .Where(p => p.Manual && p.Origin == "rule" && p.Quantity is not null)
            .ToDictionary(p => p.Id, p => p.Quantity!.Value, StringComparer.Ordinal);
        var sheet = new SurveySheet();
        var length = input.LengthM;
        var laying = input.LayingDepthM;
        var frost = input.FrostDepthM;
        var diameter = input.DiameterMm;
        var category = NormCategory(input.SoilCategory);
        var spacingChosen = input.SpacingM;
        var spacingManual = manuals.ContainsKey("spacing");
        if (spacingManual) spacingChosen = manuals["spacing"];

        var depth = DepthRange(laying, frost);
        if (manuals.TryGetValue("depth", out var depthManual))
            depth = new Range(depthManual, null);

        Range? intervals = null;
        if (length is > 0)
        {
            if (manuals.TryGetValue("intervals", out var intervalManual))
                intervals = new Range(intervalManual, null);
            else if (spacingChosen is > 0)
                intervals = new Range(Round3(length.Value / spacingChosen.Value), null);
            else
                intervals = new Range(Round3(length.Value / SpacingMaxM), Round3(length.Value / SpacingMinM));
        }

        var item = PickItem(diameter, category, depth);
        Range? meters = null;
        if (manuals.TryGetValue("meters", out var metersManual))
            meters = new Range(metersManual, null);
        else if (intervals is not null && depth is not null)
            meters = Product(intervals.Value, depth.Value);

        sheet.Parameters.Add(User("length", "Длина трассы", "м", length,
            "Длину трассы задаёт проект. Пункт 7.1.11 от неё не отказывается: среднее расстояние таблицы 7.2 применяется к этой длине."));
        sheet.Parameters.Add(User("laying", "Предполагаемая глубина заложения трубопровода", "м", laying,
            "Таблица 7.2 мерит глубину скважины от предполагаемой глубины заложения. Это исходное проекта, не результат пункта."));
        sheet.Parameters.Add(User("frost", "Нормативная глубина промерзания", "м", frost,
            "Нижняя граница глубины скважины в строке газопровода таблицы 7.2 опирается на нормативную глубину промерзания."));
        sheet.Parameters.Add(User("diameter", "Начальный диаметр скважины", "мм", diameter,
            "Пункт 54 НЗ 281/пр подбирает показатель таблицы 14 по начальному диаметру."));
        sheet.Parameters.Add(CategoryRow(category));

        sheet.Parameters.Add(RuleRange("width", "Ширина притрассовой полосы", "м", WidthMinM, WidthMaxM,
            manuals, WidthProtocol()));
        sheet.Parameters.Add(SpacingRow(spacingChosen, spacingManual));
        sheet.Parameters.Add(DepthRow(laying, frost, depth, manuals.ContainsKey("depth")));
        sheet.Parameters.Add(IntervalsRow(length, spacingChosen, intervals, manuals.ContainsKey("intervals")));
        sheet.Parameters.Add(MetersRow(meters, item, manuals.ContainsKey("meters")));
        return sheet;
    }

    public static SurveyInputs ReadInputs(SurveySheet? sheet)
    {
        if (sheet is null) return new SurveyInputs();
        return new SurveyInputs
        {
            LengthM = UserQty(sheet, "length"),
            LayingDepthM = UserQty(sheet, "laying"),
            FrostDepthM = UserQty(sheet, "frost"),
            DiameterMm = UserQty(sheet, "diameter"),
            SoilCategory = sheet.Parameters.FirstOrDefault(p => p.Id == "category")?.Text,
            SpacingM = Chosen(sheet, "spacing")
        };
    }

    public static bool IsProtectedProject(string id) =>
        id.Equals("aec242fdd3e342598f677b0bcbe9c8d4", StringComparison.OrdinalIgnoreCase)
        || id.Equals("e28ab1f595b1482ab63a21625a90644b", StringComparison.OrdinalIgnoreCase)
        || id.Equals("b57b321bb4424f58a2a0bbe20b988f4f", StringComparison.OrdinalIgnoreCase);

    private static SurveyParameter SpacingRow(decimal? chosen, bool manual)
    {
        var row = new SurveyParameter
        {
            Id = "spacing",
            Name = "Среднее расстояние между скважинами по трассе",
            Unit = "м",
            Origin = "rule",
            NormNd = SpNd,
            NormClause = "7.1.11",
            NormTable = "7.2",
            Quote = ClauseQuote(),
            Why = $"Строка «{GasRow}» таблицы 7.2: среднее расстояние 100–300 м.",
            Manual = manual
        };
        row.Protocol.Add(ClauseQuote());
        row.Protocol.Add($"Таблица 7.2, строка «{GasRow}»: среднее расстояние между инженерно-геологическими скважинами по трассе 100–300 м.");
        if (chosen is > 0)
        {
            row.Quantity = Round3(chosen.Value);
            if (manual)
                row.Protocol.Add($"Число задано вручную: {Num(chosen.Value)} м. Ссылка на пункт 7.1.11 сохранена.");
            else if (chosen < SpacingMinM || chosen > SpacingMaxM)
                row.Protocol.Add($"Расстояние {Num(chosen.Value)} м лежит вне 100–300 м таблицы 7.2 и всё равно подставлено в деление.");
            else
                row.Protocol.Add($"Для расчёта числа промежутков взято {Num(chosen.Value)} м внутри 100–300 м.");
        }
        else
        {
            row.Quantity = SpacingMinM;
            row.QuantityTo = SpacingMaxM;
            row.Protocol.Add("Одно расстояние пункт не назначает: в таблице диапазон. Число появится, когда расстояние выбрано внутри него или задано вручную.");
        }
        row.Uses.Add(RateUse(null, "От расстояния зависит число промежутков, а от него — погонные метры таблицы 14."));
        return row;
    }

    private static SurveyParameter DepthRow(decimal? laying, decimal? frost, Range? depth, bool manual)
    {
        var row = new SurveyParameter
        {
            Id = "depth",
            Name = "Глубина инженерно-геологической скважины",
            Unit = "м",
            Origin = "rule",
            NormNd = SpNd,
            NormClause = "7.1.11",
            NormTable = "7.2",
            Quote = DepthQuote(),
            Why = "Глубина скважины — третий показатель строки газопровода таблицы 7.2 и конечная глубина для таблицы 14 НЗ.",
            Manual = manual,
            Quantity = depth?.From,
            QuantityTo = depth?.To
        };
        row.Protocol.Add(ClauseQuote());
        row.Protocol.Add(DepthQuote());
        if (manual && depth?.From is decimal set)
            row.Protocol.Add($"Число задано вручную: {Num(set)} м. Ссылка на пункт 7.1.11 сохранена.");
        else if (laying is null && frost is null)
            row.Protocol.Add("Чтобы получить метры, нужны предполагаемая глубина заложения и нормативная глубина промерзания. Пункт сам их не назначает.");
        else if (depth?.From is decimal from)
        {
            var tail = depth?.To is decimal to ? $"–{Num(to)}" : "";
            row.Protocol.Add($"Подстановка: глубина скважины {Num(from)}{tail} м.");
            if (laying is decimal lay)
                row.Protocol.Add($"Глубина заложения {Num(lay)} м, плюс 1–2 м.");
            if (frost is decimal fr)
                row.Protocol.Add($"Нормативная глубина промерзания {Num(fr)} м, не менее чем на 1–2 м ниже неё.");
        }
        row.Uses.Add(RateUse(null, "Конечная глубина выбирает полосу таблицы 14 (до 15 м, 15–25 м, 25–50 м, 50–100 м)."));
        return row;
    }

    private static SurveyParameter IntervalsRow(decimal? length, decimal? spacing, Range? intervals, bool manual)
    {
        var row = new SurveyParameter
        {
            Id = "intervals",
            Name = "Число промежутков между скважинами",
            Unit = "промежуток",
            Origin = "rule",
            NormNd = SpNd,
            NormClause = "7.1.11",
            NormTable = "7.2",
            Quote = ClauseQuote(),
            Why = "Таблица 7.2 задаёт среднее расстояние. Число промежутков — деление длины трассы на это расстояние.",
            Manual = manual,
            Quantity = intervals?.From,
            QuantityTo = intervals?.To
        };
        row.Protocol.Add(ClauseQuote());
        row.Protocol.Add("Пункт 7.1.11 и таблица 7.2 не содержат формулы числа скважин. Число промежутков принято как длина трассы / среднее расстояние.");
        if (manual && intervals?.From is decimal hand)
            row.Protocol.Add($"Число задано вручную: {Num(hand)}. Ссылка на пункт 7.1.11 сохранена.");
        else if (length is > 0 && spacing is > 0 && intervals?.From is decimal one)
            row.Protocol.Add($"Подстановка: {Num(length.Value)} м / {Num(spacing.Value)} м = {Num(one)}.");
        else if (length is > 0 && intervals?.From is decimal low && intervals?.To is decimal high)
            row.Protocol.Add($"Для длины {Num(length.Value)} м при расстоянии 300 м получается {Num(low)} промежутков, при 100 м — {Num(high)}.");
        else
            row.Protocol.Add("Длина трассы не задана, число промежутков не считается.");
        row.Uses.Add(RateUse(null, "Число промежутков входит в погонные метры таблицы 14."));
        return row;
    }

    private static SurveyParameter MetersRow(Range? meters, string? item, bool manual)
    {
        var row = new SurveyParameter
        {
            Id = "meters",
            Name = "Проходка колонковым способом",
            Unit = "погонный метр",
            Origin = "rule",
            NormNd = NzNd,
            NormClause = "55",
            NormTable = "14",
            Quote = "55. Показатели затрат на полевые работы по проходке инженерно-геологических скважин колонковым способом бурения приведены в таблице 14. Единица — погонный метр.",
            Why = "Таблица 14 п. 55 НЗ 281/пр оценивает проходку в погонных метрах. Метраж собран из числа промежутков и глубины.",
            Manual = manual,
            Quantity = meters?.From,
            QuantityTo = meters?.To
        };
        row.Protocol.Add(row.Quote!);
        row.Protocol.Add("Пункт 54: таблица 14 учитывает начальный диаметр, конечную глубину скважины и категорию грунта (таблица 13 того же пункта).");
        row.Protocol.Add("Метраж = число промежутков × глубина скважины. Это подстановка в единицу таблицы 14, не формула пункта 7.1.11.");
        if (manual && meters?.From is decimal hand)
            row.Protocol.Add($"Число задано вручную: {Num(hand)} пог. м. Ссылка на пункт 55 сохранена.");
        else if (meters?.From is decimal from)
        {
            var tail = meters?.To is decimal to ? $"–{Num(to)}" : "";
            row.Protocol.Add($"Подстановка: {Num(from)}{tail} пог. м.");
        }
        else
            row.Protocol.Add("Метраж не собран: не хватает числа промежутков или глубины.");
        if (item is not null)
            row.Protocol.Add($"Полоса таблицы 14, пункт {item}.");
        else
            row.Protocol.Add("Пункт таблицы 14 не выбран: нужны диаметр, категория грунта и одна полоса конечной глубины.");
        row.Uses.Add(RateUse(item, item is null
            ? "Таблица 14, пункт определится по диаметру, глубине и категории."
            : $"Таблица 14, пункт {item}. Цена пункта в паспорт не переносилась."));
        return row;
    }

    private static SurveyParameter CategoryRow(string? category)
    {
        var row = User("category", "Категория грунта", null, null,
            "Пункт 54 НЗ 281/пр отсылает категорию грунта к таблице 13. По ней выбирается пункт таблицы 14.");
        row.Text = category;
        row.Unit = "категория";
        return row;
    }

    private static SurveyParameter User(string id, string name, string? unit, decimal? quantity, string why) => new()
    {
        Id = id,
        Name = name,
        Unit = unit,
        Origin = "user",
        Quantity = quantity is decimal q ? Round3(q) : null,
        Why = why,
        Protocol = [why]
    };

    private static SurveyParameter RuleRange(string id, string name, string unit, decimal from, decimal to, Dictionary<string, decimal> manuals, List<string> protocol)
    {
        var manual = manuals.ContainsKey(id);
        var row = new SurveyParameter
        {
            Id = id,
            Name = name,
            Unit = unit,
            Origin = "rule",
            NormNd = SpNd,
            NormClause = "7.1.11",
            NormTable = "7.2",
            Quote = ClauseQuote(),
            Why = $"Строка «{GasRow}» таблицы 7.2.",
            Manual = manual,
            Protocol = protocol
        };
        if (manual)
        {
            row.Quantity = Round3(manuals[id]);
            row.Protocol.Add($"Число задано вручную: {Num(manuals[id])} {unit}. Ссылка на пункт 7.1.11 сохранена.");
        }
        else
        {
            row.Quantity = from;
            row.QuantityTo = to;
        }
        return row;
    }

    private static List<string> WidthProtocol() =>
    [
        ClauseQuote(),
        $"Таблица 7.2, строка «{GasRow}»: ширина полосы трассы 100–200 м.",
        "Примечание 4 к таблице 7.2: большие значения ширины применяют в сложных инженерно-геологических условиях. Категорию сложности проект задаёт выбором числа внутри диапазона."
    ];

    private static string? PickItem(decimal? diameter, string? category, Range? depth)
    {
        if (diameter is not > 0 || category is null || depth?.From is not decimal from) return null;
        var to = depth?.To ?? from;
        var a = FindBand(diameter.Value, category, from);
        var b = FindBand(diameter.Value, category, to);
        if (a is not DrillBand left || b is not DrillBand right || left.Item != right.Item) return null;
        return left.Item;
    }

    private static DrillBand? FindBand(decimal diameter, string category, decimal depth)
    {
        foreach (var band in Bands)
        {
            if (!string.Equals(band.Category, category, StringComparison.OrdinalIgnoreCase)) continue;
            if (diameter <= band.DiameterFrom || diameter > band.DiameterTo) continue;
            if (depth <= band.DepthFrom || depth > band.DepthTo) continue;
            return band;
        }
        return null;
    }

    private static Range? DepthRange(decimal? laying, decimal? frost)
    {
        decimal? low = null;
        decimal? high = null;
        if (laying is decimal lay && lay >= 0)
        {
            low = lay + 1;
            high = lay + 2;
        }
        if (frost is decimal fr && fr >= 0)
        {
            var frostLow = fr + 1;
            var frostHigh = fr + 2;
            low = low is decimal l ? Math.Max(l, frostLow) : frostLow;
            high = high is decimal h ? Math.Max(h, frostHigh) : frostHigh;
        }
        if (low is not decimal from) return null;
        var to = high ?? from;
        if (from > to) to = from;
        return new Range(Round3(from), from == to ? null : Round3(to));
    }

    private static Range Product(Range intervals, Range depth)
    {
        var depths = depth.To is decimal dt ? new[] { depth.From, dt } : [depth.From];
        var counts = intervals.To is decimal it ? new[] { intervals.From, it } : [intervals.From];
        var values = counts.SelectMany(c => depths.Select(d => Round3(c * d))).ToList();
        var min = values.Min();
        var max = values.Max();
        return new Range(min, min == max ? null : max);
    }

    private static decimal? UserQty(SurveySheet sheet, string id) =>
        sheet.Parameters.FirstOrDefault(p => p.Id == id && p.Origin == "user")?.Quantity;

    private static decimal? Chosen(SurveySheet sheet, string id)
    {
        var row = sheet.Parameters.FirstOrDefault(p => p.Id == id);
        if (row?.Quantity is not decimal q || row.QuantityTo is not null) return null;
        return q;
    }

    private static SurveyRateUse RateUse(string? item, string note) => new()
    {
        BookId = NzBookId,
        OrderRef = "№281/пр от 12.05.2025",
        Nd = NzNd,
        Table = "14",
        Item = item,
        Note = note
    };

    private static string ClauseQuote() =>
        "7.1.11 Ширину притрассовой полосы линейного сооружения, среднее расстояние между инженерно-геологическими скважинами и их глубину следует принимать в соответствии с таблицей 7.2.";

    private static string DepthQuote() =>
        "Таблица 7.2, строка «Водопровод, канализация, теплосеть и газопровод»: на 1–2 м ниже предполагаемой глубины заложения трубопровода (шпунта, острия свай, колодца, камеры), но не менее чем на 1–2 м ниже нормативной глубины промерзания грунта.";

    private static string? NormCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().ToUpperInvariant();
        if (int.TryParse(text, out var n) && n is >= 1 and <= 9) return Categories[n - 1];
        return Categories.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
    }

    private static decimal Round3(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);

    private static string Num(decimal value) =>
        Round3(value).ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));

    private readonly record struct Range(decimal From, decimal? To);

    private readonly record struct DrillBand(decimal DiameterFrom, decimal DiameterTo, decimal DepthFrom, decimal DepthTo, string Category, string Item);
}

public sealed class SurveySheet
{
    public string Kind { get; set; } = SurveyGeology.Kind;
    public string Title { get; set; } = "Инженерно-геологические изыскания, линейный газопровод";
    public List<SurveyParameter> Parameters { get; set; } = [];
}

public sealed class SurveyParameter
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    /// <summary>user — число проекта; rule — пункт СП или подстановка в него.</summary>
    public string Origin { get; set; } = "user";
    public decimal? Quantity { get; set; }
    public decimal? QuantityTo { get; set; }
    public string? Text { get; set; }
    public bool Manual { get; set; }
    public string? NormNd { get; set; }
    public string? NormClause { get; set; }
    public string? NormTable { get; set; }
    public string? Quote { get; set; }
    public string? Why { get; set; }
    public List<SurveyRateUse> Uses { get; set; } = [];
    public List<string> Protocol { get; set; } = [];
}

public sealed class SurveyRateUse
{
    public string BookId { get; set; } = "";
    public string OrderRef { get; set; } = "";
    public string Nd { get; set; } = "";
    public string Table { get; set; } = "";
    public string? Item { get; set; }
    public string Note { get; set; } = "";
}

public sealed class SurveyInputs
{
    public decimal? LengthM { get; set; }
    public decimal? LayingDepthM { get; set; }
    public decimal? FrostDepthM { get; set; }
    public decimal? SpacingM { get; set; }
    public decimal? DiameterMm { get; set; }
    public string? SoilCategory { get; set; }
}
