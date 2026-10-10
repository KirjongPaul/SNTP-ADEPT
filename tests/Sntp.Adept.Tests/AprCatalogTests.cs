using Sntp.Adept.Domain.Catalog;

namespace Sntp.Adept.Tests;

public class AprCatalogTests
{
    [Fact]
    public void Parses_Doctype_And_KoeffTypes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "koeffs-mini.apr");
        if (!File.Exists(path))
            path = Path.Combine(TestPaths.RepoRoot(), "tests", "Sntp.Adept.Tests", "fixtures", "koeffs-mini.apr");
        Assert.True(File.Exists(path), path);
        var r = AprXml.LoadFile(path);
        Assert.Null(r.Error);
        Assert.Equal("main", r.Root);
        Assert.NotEmpty(r.Koeffs);
        Assert.Contains(r.Koeffs, k => k.TypeName == "Ценообразующий" && k.Value == 0.6m);
        Assert.Contains(r.Koeffs, k => k.GroupName != null && k.GroupName.Contains("849"));
    }

    [Fact]
    public void RateCode_FromNzJustification()
    {
        var b = new Domain.Models.Basis { Kind = "NZ", Doc = "849/pr", Table = "3.8", Item = "10", Raw = "НЗ №849/пр табл. 3.8 п.10" };
        Assert.Equal("NZ-849-3.8-10", AprXml.RateCode(b));
        var dotted = new Domain.Models.Basis { Kind = "NZ", Doc = "849/pr", Table = "3.8.", Item = "10." };
        Assert.Equal("NZ-849-3.8-10", AprXml.RateCode(dotted));
    }

    [Fact]
    public void LiveApr_Koeffs_IfAdeptInstalled()
    {
        var path = @"C:\Adept\AUS-client\catalogs\koeffs.apr";
        if (!File.Exists(path))
            return;
        var r = AprXml.LoadFile(path);
        Assert.Null(r.Error);
        Assert.True(r.Koeffs.Count > 100, $"koeffs={r.Koeffs.Count}");
        Assert.Contains(r.Koeffs, k => k.TypeName == "Усложняющий");
        Assert.Contains(r.Koeffs, k => k.TypeName == "Составной");
    }
}
