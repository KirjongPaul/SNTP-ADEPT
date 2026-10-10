using System.Text.RegularExpressions;

namespace Sntp.Adept.Domain.Books;

/// <summary>Работа, не учтённая ценой сборника. Её можно открыть отдельной локальной сметой.</summary>
public sealed class EstimateOffer
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public string BookTitle { get; set; } = "";
    public string ClauseId { get; set; } = "";
    /// <summary>Буква пункта, например «а».</summary>
    public string Mark { get; set; } = "";
    /// <summary>Текст пункта приказа.</summary>
    public string Text { get; set; } = "";
    /// <summary>Короткое имя сметы, пока его не заменили в справочнике.</summary>
    public string ShortTitle { get; set; } = "";
}

public static class EstimateOffers
{
    public static List<EstimateOffer> From(ReferenceBook book)
    {
        var list = new List<EstimateOffer>();
        foreach (var clause in book.GeneralPart)
        {
            if (!clause.Text.Contains("не учт", StringComparison.OrdinalIgnoreCase)) continue;
            var points = Points(clause.Text);
            if (points.Count == 0)
            {
                var title = FirstSentence(clause.Text);
                if (title.Length > 0) list.Add(Make(book, clause.Id, 0, "", title));
                continue;
            }
            for (var i = 0; i < points.Count; i++)
                list.Add(Make(book, clause.Id, i, points[i].Letter, points[i].Text));
        }
        return list;
    }

    public static string ShortName(string point)
    {
        var raw = point.Trim();
        foreach (var (needle, title) in KnownTitles)
        {
            if (raw.Contains(needle, StringComparison.OrdinalIgnoreCase)) return title;
        }
        var text = raw.TrimEnd('.', ';');
        string[] prefixes =
        [
            "по проектированию ",
            "по подготовке проектной и рабочей документации ",
            "по подготовке проекта ",
            "по подготовке проектных решений ",
            "по подготовке мероприятий ",
            "по подготовке и согласованию документации ",
            "по подготовке ",
            "по проведению ",
            "по разработке ",
            "на выполнение ",
            "на работы, ",
            "проектирование ",
            "по "
        ];
        var lower = text.ToLowerInvariant();
        foreach (var prefix in prefixes.OrderByDescending(p => p.Length))
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal)) continue;
            text = text[prefix.Length..].Trim();
            break;
        }
        var low = text.ToLowerInvariant();
        var end = text.Length;
        foreach (var cut in new[] { " (далее", " (", ", ", " а также", " включая", " из зоны", " на период", " на участок" })
        {
            var at = low.IndexOf(cut, StringComparison.Ordinal);
            if (at > 8 && at < end) end = at;
        }
        text = TrimTail(text[..end].Trim());
        if (text.Length == 0) return TrimTail(raw);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static readonly (string Needle, string Title)[] KnownTitles =
    [
        ("переустройств", "Переустройство и вынос надземных и подземных сооружений и коммуникаций"),
        ("рекультивац", "Рекультивация нарушенных территорий"),
        ("санитарно-защитн", "Организация санитарно-защитной зоны"),
        ("инженерной защите", "Инженерная защита земельных участков"),
        ("дендроплан", "Дендроплан и перечётная ведомость"),
        ("технического обследования", "Техническое обследование и сохранность зданий и сооружений"),
        ("дорожного движения", "Организация дорожного движения"),
        ("водозабор", "Водозаборные и очистные сооружения"),
        ("карьер", "Карьеры общераспространённых полезных ископаемых"),
        ("скважин", "Скважины различного назначения"),
        ("электростанц", "Электростанции и электроподстанции"),
        ("электрообогрев", "Электрообогрев конструкций, оборудования и трубопроводов"),
        ("прожекторн", "Наружное освещение"),
        ("пожаротушен", "Установки пожаротушения"),
        ("вахтовых", "Вахтовые жилые посёлки"),
        ("акустическ", "Акустические расчёты и самокомпенсация трубопроводов"),
        ("застройщика", "Выполнение функций застройщика"),
        ("газораспределен", "Газораспределение и газопотребление"),
        ("крупногабаритн", "Транспортировка крупногабаритного оборудования"),
        ("антитеррорист", "Антитеррористическая защищённость"),
        ("метрологическ", "Метрологическое обеспечение"),
        ("памятник", "Охрана памятников археологии"),
        ("таблице 3.10", "Работы по таблице 3.10"),
        ("переход", "Переходы через реки")
    ];

    private static string TrimTail(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        string[] tails = ["по", "на", "из", "для", "от", "с", "со", "в", "во", "к", "о", "об", "и", "а", "при"];
        while (words.Count > 1 && tails.Contains(words[^1], StringComparer.OrdinalIgnoreCase))
            words.RemoveAt(words.Count - 1);
        if (words.Count > 6) words = words.Take(6).ToList();
        while (words.Count > 1 && tails.Contains(words[^1], StringComparer.OrdinalIgnoreCase))
            words.RemoveAt(words.Count - 1);
        return string.Join(' ', words);
    }

    private static EstimateOffer Make(ReferenceBook book, string clauseId, int index, string mark, string text) => new()
    {
        Id = $"{book.Id}:{clauseId}:{index}",
        BookId = book.Id,
        BookTitle = string.IsNullOrWhiteSpace(book.Title) ? book.Id : book.Title,
        ClauseId = clauseId,
        Mark = mark,
        Text = text.Length > 240 ? text[..240].TrimEnd() + "…" : text,
        ShortTitle = ShortName(text)
    };

    private static string FirstSentence(string text)
    {
        var cut = text.Split(['.', ';'], 2)[0].Trim();
        return cut.Length > 180 ? cut[..180].TrimEnd() + "…" : cut;
    }

    private static List<(string Letter, string Text)> Points(string text)
    {
        var normalized = text.Replace(" ж по ", " ж) по ");
        var marks = new List<(int Index, int Length, string Letter)>();
        foreach (Match match in Regex.Matches(normalized, @"(?:^|[\n;]|\s)([а-яё])\)\s+", RegexOptions.IgnoreCase))
            marks.Add((match.Index, match.Length, match.Groups[1].Value.ToLowerInvariant()));
        if (marks.Count < 2) return [];
        var items = new List<(string Letter, string Text)>();
        for (var i = 0; i < marks.Count; i++)
        {
            var start = marks[i].Index + marks[i].Length;
            var end = i + 1 < marks.Count ? marks[i + 1].Index : normalized.Length;
            var item = normalized[start..end].Trim().TrimEnd(';', ' ', '\n');
            if (item.Length > 0) items.Add((marks[i].Letter, item));
        }
        return items;
    }
}
