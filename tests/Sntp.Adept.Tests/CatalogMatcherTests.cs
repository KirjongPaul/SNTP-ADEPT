using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class CatalogMatcherTests
{
    [Fact]
    public void Exact_Code_Is_Matched_Fuzzy_Is_Partial_Many_Are_Ambiguous()
    {
        var rates = new List<CatalogCandidate>
        {
            new() { Code = "NZ-849-3.8-10", Name = "Дорога", Doc = "849/pr", Table = "3.8", Item = "10", Source = "excel-seed", PriceA = 1, PriceB = 2 },
            new() { Code = "SBC-81-2001-17-1-16", Name = "Газопровод", Doc = "СБЦП 81-2001-17", Table = "1", Item = "16", Source = "adept-ui", PriceA = 10 },
            new() { Code = "SBC-81-2001-17-1-17", Name = "Газопровод второй", Doc = "СБЦП 81-2001-17", Table = "1", Item = "17", Source = "adept-ui", PriceA = 11 }
        };

        var hit = CatalogMatcher.Search("НЗ №849/пр табл. 3.8 п.10", rates);
        Assert.Equal("matched", hit.Status);
        Assert.Equal("NZ-849-3.8-10", hit.Hits[0].Code);
        Assert.True(hit.Hits[0].HasPrices);

        var partial = CatalogMatcher.Search("Газопровод", rates);
        Assert.Contains(partial.Status, new[] { "partial", "ambiguous", "matched" });

        var fgis = CatalogMatcher.Search("нет такого шифра совсем", []);
        Assert.Equal("catalog-miss", fgis.Status);
    }

    [Fact]
    public void Apply_Does_Not_Invent_Prices()
    {
        var pos = new Position { Name = "Работа" };
        CatalogMatcher.ApplyHit(pos, new LineHit
        {
            Status = "matched",
            Code = "СБЦП 81-2001-17",
            Name = "Сборник",
            Source = "fgis",
            HasPrices = false,
            Kind = "SBC"
        });
        Assert.Null(pos.PriceA);
        Assert.Equal("СБЦП 81-2001-17", pos.CatalogCode);

        CatalogMatcher.ApplyHit(pos, new LineHit
        {
            Status = "matched",
            Code = "NZ-849-3.8-10",
            Name = "Дорога",
            Source = "excel-seed",
            HasPrices = true,
            PriceA = 31100,
            PriceB = 122993,
            Kind = "NZ"
        });
        Assert.Equal(31100, pos.PriceA);
        Assert.Equal(122993, pos.PriceB);
    }
}
