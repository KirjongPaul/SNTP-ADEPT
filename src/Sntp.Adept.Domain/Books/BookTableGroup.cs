using System.Text.RegularExpressions;

namespace Sntp.Adept.Domain.Books;

/// <summary>
/// Группа пункта таблицы цены. Сама знает имя объёма работ и какой полосой
/// или укрупнённой строкой отвечать на показатель. Наследник перегружает
/// имя, признак укрупнения и постановку — так позже привяжут правила изысканий,
/// не трогая формулы a/b.
/// </summary>
public class BookTableGroup
{
    public BookTableGroup(string table, string item, string sourceName, IReadOnlyList<BookItem> bands)
    {
        Table = table;
        Item = item;
        SourceName = sourceName ?? "";
        Bands = bands;
    }

    public string Table { get; }
    public string Item { get; }
    /// <summary>Название из сборника, как в book.json.</summary>
    public string SourceName { get; }
    public IReadOnlyList<BookItem> Bands { get; }

    /// <summary>Имя группы для объёмов работ: без метки «объём работ» и без хвостового двоеточия.</summary>
    public virtual string VolumeName
    {
        get
        {
            var title = BookGroupNaming.From(SourceName);
            return title.Length > 0 ? title : SourceName.Trim();
        }
    }

    /// <summary>indicator — по полосе показателя; lump — все строки укрупнённые; mixed — оба способа в одной группе.</summary>
    public virtual string SelectKind
    {
        get
        {
            if (Bands.Count == 0) return "indicator";
            var lump = Bands.Count(IsLump);
            if (lump == 0) return "indicator";
            if (lump == Bands.Count) return "lump";
            return "mixed";
        }
    }

    /// <summary>Единица показателя. У смешанной группы это единица полосы, не «объект».</summary>
    public virtual string? IndicatorUnit
    {
        get
        {
            var measured = Bands.FirstOrDefault(b => !IsLump(b) && !string.IsNullOrWhiteSpace(b.Unit));
            if (measured?.Unit is { Length: > 0 } unit) return unit;
            return Bands.Select(b => b.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        }
    }

    /// <summary>Укрупнённая расценка: единица «объект» и нет параметра «в». Величина показателя её не меняет.</summary>
    public virtual bool IsLump(BookItem item) => BookGroupNaming.IsLump(item);

    /// <summary>
    /// Постановка группы. Сейчас совпадает с именем объёма.
    /// Наследник подставит состав изысканий, когда правила СП будут привязаны к пункту.
    /// </summary>
    public virtual string Statement() => VolumeName;

    public static List<BookTableGroup> Split(BookTable table)
    {
        var list = new List<BookTableGroup>();
        List<BookItem>? bands = null;
        string? item = null;
        string? name = null;
        foreach (var row in table.Items)
        {
            var n = BookText.NormItem(row.N);
            if (bands is not null && n == item && string.Equals(name, row.Name, StringComparison.Ordinal))
            {
                bands.Add(row);
                continue;
            }
            bands = [row];
            item = n;
            name = row.Name;
            list.Add(new BookTableGroup(table.Code, n, row.Name ?? "", bands));
        }
        return list;
    }

    /// <summary>
    /// Группа пункта. Имя объёма сравнивается и с сырым названием, и с очищенным.
    /// Если имя не совпало ни с одной группой, возвращаются все полосы пункта — как раньше.
    /// </summary>
    public static BookTableGroup? For(BookTable table, string itemNo, string? volumeName)
    {
        var cited = BookText.NormItem(itemNo);
        var groups = Split(table).Where(g => g.Item == cited).ToList();
        if (groups.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(volumeName))
        {
            var asked = BookGroupNaming.From(volumeName);
            var named = groups.Where(g =>
                string.Equals(g.SourceName, volumeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(g.VolumeName, volumeName, StringComparison.OrdinalIgnoreCase)
                || (asked.Length > 0 && string.Equals(g.VolumeName, asked, StringComparison.OrdinalIgnoreCase))).ToList();
            if (named.Count > 0)
                return named.Count == 1
                    ? named[0]
                    : new BookTableGroup(table.Code, cited, named[0].SourceName, named.SelectMany(g => g.Bands).ToList());
        }
        if (groups.Count == 1) return groups[0];
        return new BookTableGroup(table.Code, cited, groups[0].SourceName, groups.SelectMany(g => g.Bands).ToList());
    }
}

public static class BookGroupNaming
{
    /// <summary>
    /// «ОБЪЁМ РАБОТ п. 1. Магистральный газопровод … протяженностью:»
    /// становится «Магистральный газопровод … протяженностью».
    /// Метка интерфейса снимается только с начала строки.
    /// </summary>
    public static string From(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return "";
        var fold = Fold(s);
        const string label = "объем работ";
        if (fold.StartsWith(label, StringComparison.Ordinal))
        {
            s = s[label.Length..].TrimStart();
            fold = Fold(s);
        }
        if (fold.StartsWith("п.", StringComparison.Ordinal))
        {
            var mark = Regex.Match(s, @"^[Пп]\.\s*[\d.]+\.?\s*");
            if (mark.Success) s = s[mark.Length..];
        }
        return s.Trim().TrimEnd(':', '：').Trim();
    }

    public static bool IsLump(BookItem item)
    {
        if (item.B is not null) return false;
        var unit = item.Unit ?? "";
        return unit.Contains("объект", StringComparison.OrdinalIgnoreCase);
    }

    private static string Fold(string value) =>
        value.Replace('ё', 'е').Replace('Ё', 'Е').ToLowerInvariant();
}
