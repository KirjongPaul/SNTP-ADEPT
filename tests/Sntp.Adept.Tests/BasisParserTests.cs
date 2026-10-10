using Sntp.Adept.Domain.Catalog;

namespace Sntp.Adept.Tests;

public class BasisParserTests
{
    [Fact]
    public void Long_Equals_Short_And_Suffix()
    {
        var longForm = BasisParser.Parse("СБЦП 81-02-17-2001");
        var shortForm = BasisParser.Parse("СБЦП 81-2001-17");
        Assert.Equal("СБЦП 81-2001-17", longForm.CanonicalDoc);
        Assert.Equal(shortForm.CanonicalDoc, longForm.CanonicalDoc);
        Assert.Equal(17, longForm.CollectionNum);
        Assert.Equal("SBC-81-2001-17", AprXml.RateCode(longForm));

        var suffix = BasisParser.Parse("СБЦП 81-02-17-2001_0-0-1-16");
        Assert.Equal("0", suffix.Part);
        Assert.Equal("0", suffix.Chapter);
        Assert.Equal("1", suffix.Table);
        Assert.Equal("16", suffix.Item);
        Assert.Equal("adept-suffix", suffix.ParsePattern);
        Assert.Equal("SBC-81-2001-17-1-16", AprXml.RateCode(suffix));
    }

    [Fact]
    public void Grand_Nz_Mrr_Asutp_Survey()
    {
        var grand = BasisParser.Parse("СБЦП19-5-1");
        Assert.Equal(19, grand.CollectionNum);
        Assert.Equal("5", grand.Table);
        Assert.Equal("1", grand.Item);
        Assert.Equal("grand-compact", grand.ParsePattern);

        var nz = BasisParser.Parse("НЗ №849/пр табл. 3.8 п.10");
        Assert.Equal("NZ", nz.Kind);
        Assert.Equal("849/pr", nz.Doc);
        Assert.Equal("3.8", nz.Table);
        Assert.Equal("10", nz.Item);
        Assert.Equal("NZ-849-3.8-10", AprXml.RateCode(nz));

        var mrr = BasisParser.Parse("МРР-9.1.02-18");
        Assert.Equal("MRR", mrr.Kind);
        Assert.Equal("МРР-9.1.02-18", mrr.CanonicalDoc);

        var asutp = BasisParser.Parse("СБЦ АСУТП 2016");
        Assert.Equal("СБЦП 81-2001-22", asutp.CanonicalDoc);
        Assert.Equal("SBC-81-2001-22", AprXml.RateCode(asutp));

        var parent = BasisParser.Parse("СБЦи5.1_0-1-7(1)");
        Assert.Equal("SBCI", parent.Kind);
        Assert.Equal("СБЦи5.1_0-1-7(1)", parent.AdeptCode);
        var child = AdeptUiRate.FromGrid("Изыскательские работы, ФРСН / Таблица 07", "СБЦи5.1_0-1-7-1-1", "Категория грунтов I", null, null, null, null);
        Assert.Equal("СБЦи5.1_0-1-7", child.ParentCode);
        Assert.True(child.Qualifier);
    }

    [Fact]
    public void Latin_Sbcp_Normalizes()
    {
        var b = BasisParser.Parse("SBCP 81-2001-17");
        Assert.Equal("СБЦП 81-2001-17", b.CanonicalDoc);
    }
}
