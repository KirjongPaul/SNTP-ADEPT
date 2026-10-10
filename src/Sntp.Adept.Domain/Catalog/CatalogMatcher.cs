using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Catalog;

public sealed class CatalogCandidate
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Doc { get; set; }
    public string? Table { get; set; }
    public string? Item { get; set; }
    public string? GroupPath { get; set; }
    public string? Kind { get; set; }
    public decimal? PriceA { get; set; }
    public decimal? PriceB { get; set; }
    public string Source { get; set; } = "";
    public string? Raw { get; set; }
}

public sealed class LineHit
{
    public string Status { get; set; } = "catalog-miss";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? GroupPath { get; set; }
    public decimal? PriceA { get; set; }
    public decimal? PriceB { get; set; }
    public string Source { get; set; } = "";
    public string? Doc { get; set; }
    public string? Table { get; set; }
    public string? Item { get; set; }
    public string? Kind { get; set; }
    public bool HasPrices { get; set; }
}

public sealed class VendorCoeff
{
    public string Identifier { get; set; } = "";
    public string Caption { get; set; } = "";
    public decimal? Value { get; set; }
    public string? Role { get; set; }
    public string Source { get; set; } = "";
    public string? LetterRef { get; set; }
}

public sealed class LineSearchResult
{
    public Basis Basis { get; set; } = new();
    public string Status { get; set; } = "catalog-miss";
    public List<LineHit> Hits { get; set; } = [];
    public List<VendorCoeff> Vendor { get; set; } = [];
}

public static class CatalogMatcher
{
    public static LineSearchResult Search(string? text, IReadOnlyList<CatalogCandidate> rates, IReadOnlyList<VendorCoeff>? vendor = null)
    {
        var basis = BasisParser.Parse(text);
        var hits = Rank(basis, text, rates);
        var status = hits.Count == 0
            ? "catalog-miss"
            : hits[0].Status == "ambiguous" ? "ambiguous" : hits[0].Status;
        if (hits.Count > 1 && hits.All(h => h.Status == "matched"))
            status = "ambiguous";
        basis.MatchStatus = status;
        var result = new LineSearchResult { Basis = basis, Status = status, Hits = hits };
        if (!string.IsNullOrWhiteSpace(text) && text.Contains("Газпром", StringComparison.OrdinalIgnoreCase) && vendor is not null)
            result.Vendor.AddRange(vendor);
        return result;
    }

    public static List<LineHit> Rank(Basis basis, string? name, IReadOnlyList<CatalogCandidate> rates)
    {
        var exact = new List<CatalogCandidate>();
        var structured = new List<CatalogCandidate>();
        var fuzzy = new List<(CatalogCandidate Rate, double Score)>();
        var code = basis.AdeptCode ?? "";
        var rateCode = AprXml.RateCode(basis);

        foreach (var rate in rates)
        {
            if (Same(rate.Code, code) || Same(rate.Code, rateCode) || Same(rate.Code, basis.Raw))
            {
                exact.Add(rate);
                continue;
            }

            if (SameDoc(basis, rate) && Same(rate.Table, basis.Table) && Same(rate.Item, basis.Item)
                && !string.IsNullOrWhiteSpace(basis.Table))
            {
                structured.Add(rate);
                continue;
            }

            var score = ScoreName(name, basis, rate);
            if (score >= 0.34 && SameDoc(basis, rate))
                fuzzy.Add((rate, score));
        }

        if (exact.Count == 1)
            return [ToHit(exact[0], "matched")];
        if (exact.Count > 1)
            return exact.Select(r => ToHit(r, "ambiguous")).ToList();
        if (structured.Count == 1)
            return [ToHit(structured[0], "matched")];
        if (structured.Count > 1)
            return structured.Select(r => ToHit(r, "ambiguous")).ToList();

        var top = fuzzy.OrderByDescending(x => x.Score).Take(8).ToList();
        if (top.Count == 0 && string.IsNullOrWhiteSpace(basis.CanonicalDoc) && basis.Kind is "Unknown" or "")
        {
            foreach (var rate in rates)
            {
                var score = ScoreName(name, basis, rate);
                if (score >= 0.5) top.Add((rate, score));
            }
            top = top.OrderByDescending(x => x.Score).Take(8).ToList();
        }
        if (top.Count == 0) return [];
        if (top.Count > 1 && Math.Abs(top[0].Score - top[1].Score) < 0.05)
            return top.Select(x => ToHit(x.Rate, "ambiguous")).ToList();
        return top.Select(x => ToHit(x.Rate, "partial")).ToList();
    }

    public static void ApplyHit(Position pos, LineHit hit)
    {
        pos.Basis ??= new Basis();
        if (!string.IsNullOrWhiteSpace(hit.Kind)) pos.Basis.Kind = hit.Kind!;
        pos.Basis.Doc = hit.Doc ?? pos.Basis.Doc;
        pos.Basis.Table = hit.Table ?? pos.Basis.Table;
        pos.Basis.Item = hit.Item ?? pos.Basis.Item;
        pos.Basis.MatchStatus = hit.Status;
        pos.CatalogCode = hit.Code;
        pos.Code = string.IsNullOrWhiteSpace(pos.Code) ? hit.Code : pos.Code;
        if (string.IsNullOrWhiteSpace(pos.Name)) pos.Name = hit.Name;
        if (hit.HasPrices)
        {
            if (hit.PriceA is not null) pos.PriceA = hit.PriceA;
            if (hit.PriceB is not null) pos.PriceB = hit.PriceB;
        }
    }

    private static LineHit ToHit(CatalogCandidate rate, string status) => new()
    {
        Status = status,
        Code = rate.Code,
        Name = rate.Name,
        GroupPath = rate.GroupPath,
        PriceA = rate.PriceA,
        PriceB = rate.PriceB,
        Source = rate.Source,
        Doc = rate.Doc,
        Table = rate.Table,
        Item = rate.Item,
        Kind = rate.Kind,
        HasPrices = rate.PriceA is not null || rate.PriceB is not null
    };

    private static bool SameDoc(Basis basis, CatalogCandidate rate)
    {
        if (!string.IsNullOrWhiteSpace(basis.CanonicalDoc) && Contains(rate.Doc, basis.CanonicalDoc)) return true;
        if (!string.IsNullOrWhiteSpace(basis.Doc) && Contains(rate.Doc, basis.Doc)) return true;
        if (basis.CollectionNum is int nn && (rate.Doc?.Contains(nn.ToString(), StringComparison.Ordinal) == true
                                               || rate.Code.Contains("81-2001-" + nn, StringComparison.Ordinal)))
            return true;
        return false;
    }

    private static double ScoreName(string? text, Basis basis, CatalogCandidate rate)
    {
        var left = Tokens((text ?? "") + " " + (basis.Raw ?? ""));
        var right = Tokens(rate.Name + " " + (rate.Raw ?? ""));
        if (left.Count == 0 || right.Count == 0) return 0;
        var inter = left.Intersect(right).Count();
        return (double)inter / Math.Max(left.Count, right.Count);
    }

    private static HashSet<string> Tokens(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in text.Split([' ', ',', '.', ';', ':', '/', '\\', '-', '—', '(', ')', '«', '»'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 3) continue;
            set.Add(part);
        }
        return set;
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(AprXml.CleanRef(a), AprXml.CleanRef(b), StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? hay, string needle) =>
        !string.IsNullOrWhiteSpace(hay) && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
