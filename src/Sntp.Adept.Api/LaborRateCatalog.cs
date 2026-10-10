using Sntp.Adept.Domain.Json;

namespace Sntp.Adept.Api;

public static class LaborRateCatalog
{
    private static readonly object Gate = new();

    public static void AssignRole(string booksRoot, string wageCatalogId, string wagePostId, string? roleId)
    {
        lock (Gate)
        {
            var path = Path.Combine(booksRoot, "labor-rates.json");
            var file = System.Text.Json.JsonSerializer.Deserialize<LaborRateFile>(File.ReadAllText(path), PirJson.Options)
                       ?? throw new InvalidOperationException("Нет файла справочников 3П");
            var wage = file.Catalogs.FirstOrDefault(c => c.Id == wageCatalogId)
                       ?? throw new InvalidOperationException("Справочник ставок не найден");
            var post = wage.Posts.FirstOrDefault(p => p.Id == wagePostId)
                       ?? throw new InvalidOperationException("Ставка не найдена");
            var next = roleId?.Trim() ?? "";
            if (next.Length > 0)
            {
                foreach (var other in wage.Posts)
                    if (other.RoleId == next) other.RoleId = "";
            }
            post.RoleId = next;
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file, PirJson.Options));
        }
    }

    public static void AssignLink(string booksRoot, string groupCatalogId, string groupPostId, string wageCatalogId, string? wagePostId)
    {
        lock (Gate)
        {
            var path = Path.Combine(booksRoot, "labor-rates.json");
            var file = System.Text.Json.JsonSerializer.Deserialize<LaborRateFile>(File.ReadAllText(path), PirJson.Options)
                       ?? throw new InvalidOperationException("Нет файла справочников 3П");
            var groups = file.Catalogs.FirstOrDefault(c => c.Id == groupCatalogId)
                         ?? throw new InvalidOperationException("Набор групп не найден");
            if (groups.Kind != "group") throw new InvalidOperationException("Сопоставление хранится у набора групп");
            var group = groups.Posts.FirstOrDefault(p => p.Id == groupPostId)
                        ?? throw new InvalidOperationException("Группа не найдена");
            var wage = file.Catalogs.FirstOrDefault(c => c.Id == wageCatalogId)
                       ?? throw new InvalidOperationException("Приказ не найден");
            if (wage.Kind is not ("" or "wage")) throw new InvalidOperationException("Сопоставлять можно только с приказом");
            var next = wagePostId?.Trim() ?? "";
            if (next.Length > 0 && wage.Posts.All(p => p.Id != next))
                throw new InvalidOperationException("Должность приказа не найдена");
            foreach (var other in groups.Posts)
            {
                if (other.Links is null || !other.Links.TryGetValue(wageCatalogId, out var taken) || taken != next || next.Length == 0)
                    continue;
                other.Links.Remove(wageCatalogId);
                if (other.Links.Count == 0) other.Links = null;
            }
            group.Links ??= new Dictionary<string, string>();
            if (next.Length == 0) group.Links.Remove(wageCatalogId);
            else group.Links[wageCatalogId] = next;
            if (group.Links.Count == 0) group.Links = null;
            groups.RateCatalogId = wageCatalogId;
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(file, PirJson.Options));
        }
    }

    public static IReadOnlyList<LaborRateBook> Read(string booksRoot)
    {
        var path = Path.Combine(booksRoot, "labor-rates.json");
        if (!File.Exists(path)) return [];
        try
        {
            var file = System.Text.Json.JsonSerializer.Deserialize<LaborRateFile>(File.ReadAllText(path), PirJson.Options);
            return file?.Catalogs ?? [];
        }
        catch
        {
            return [];
        }
    }

    private sealed class LaborRateFile
    {
        public List<LaborRateBook> Catalogs { get; set; } = [];
    }
}

public sealed class LaborRateBook
{
    public string Id { get; set; } = "";
    /// <summary>wage — дневной оклад в Daily; hours — чел·ч на лист в Daily; index — индекс ставки в Daily.</summary>
    public string Kind { get; set; } = "wage";
    public string Title { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Order { get; set; } = "";
    public string ValidFrom { get; set; } = "";
    /// <summary>Для типовых групп: справочник ставок, откуда берётся ссылочная стоимость.</summary>
    public string RateCatalogId { get; set; } = "";
    public List<LaborRatePost> Posts { get; set; } = [];
}

public sealed class LaborRatePost
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string Grade { get; set; } = "";
    /// <summary>Формат листа для норматива чел·ч: А4, А1.</summary>
    public string Format { get; set; } = "";
    public decimal Daily { get; set; }
    public decimal Monthly { get; set; }
    /// <summary>Типовая группа должностей, к которой привязана эта ставка. Одна группа — одна ставка.</summary>
    public string RoleId { get; set; } = "";
    /// <summary>Для группы: приказ → должность этого приказа. В одном приказе должность принадлежит одной группе.</summary>
    public Dictionary<string, string>? Links { get; set; }
}
