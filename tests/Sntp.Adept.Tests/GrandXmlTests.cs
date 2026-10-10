using System.Text;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Export;

namespace Sntp.Adept.Tests;

public class GrandXmlTests
{
    public GrandXmlTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public void ParseSample_Codes_Factors_Totals()
    {
        var path = TestPaths.Sample("project-estimate.xml");
        Assert.True(File.Exists(path), path);
        var doc = GrandXml.LoadFile(path);
        Assert.Equal(GrandXml.ProjectEstimateType, doc.Grand?.DocumentType);
        Assert.Single(doc.Estimates);
        var est = doc.Estimates[0];
        Assert.Equal(2, est.Positions.Count(p => p.Kind == Sntp.Adept.Domain.Models.PositionKind.Work));
        Assert.Equal(Sntp.Adept.Domain.Models.PositionKind.Total, est.Positions[^1].Kind);
        Assert.Equal("СБЦП19-5-1", est.Positions[0].Code);
        Assert.Equal("СБЦП16-1-1.11-1", est.Positions[1].Code);
        Assert.Contains(est.Factors, f => f.Identifier == "Ки1" && f.ProjectDoc == 0.4m && f.WorkDoc == 0.6m);
        Assert.Equal(1000m, est.Positions[0].Quantity);
        Assert.Equal(6930m, est.Positions[0].ExcelAmount);
        Assert.Equal(894180m, est.Positions[1].ExcelAmount);
        Assert.Equal(6930m, est.Positions[0].EngineAmount);
        Assert.Equal(894180m, est.Positions[1].EngineAmount);
    }

    [Fact]
    public void Gsfx_StripsSign_SameTotals()
    {
        var path = TestPaths.Sample("project-estimate.gsfx");
        Assert.True(File.Exists(path), path);
        var doc = GrandXml.LoadGsfx(path);
        Assert.Equal(6930m, doc.Estimates[0].Positions[0].EngineAmount);
        Assert.Equal(894180m, doc.Estimates[0].Positions[1].EngineAmount);
        var xml = Encoding.GetEncoding(1251).GetString(GrandXml.WriteUnsignedXml(doc));
        Assert.DoesNotContain("GrandSmetaSign", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("39200715-3435-47E0-8C27-68A028AD6D25", xml);
        Assert.DoesNotContain("2B0470FD", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoundTrip_Codes_And_Totals()
    {
        var src = GrandXml.LoadFile(TestPaths.Sample("project-estimate.xml"));
        var bytes = GrandXml.WriteUnsignedXml(src);
        var again = GrandXml.LoadXml(bytes, "roundtrip.xml");
        Assert.Equal(src.Estimates[0].Positions[0].Code, again.Estimates[0].Positions[0].Code);
        Assert.Equal(src.Estimates[0].Positions[1].Code, again.Estimates[0].Positions[1].Code);
        Assert.Equal(6930m, again.Estimates[0].Positions[0].EngineAmount);
        Assert.Equal(894180m, again.Estimates[0].Positions[1].EngineAmount);
        Assert.Contains(again.Estimates[0].Factors, f => f.Identifier == "Ки1");
        Assert.Equal(src.Estimates[0].Positions[0].Factors[0].Identifier, again.Estimates[0].Positions[0].Factors[0].Identifier);
    }

    [Fact]
    public void Import_Recalc_Export_Integration()
    {
        var doc = GrandXml.LoadFile(TestPaths.Sample("project-estimate.xml"));
        CalcEngine.Recalculate(doc);
        var tmp = Path.Combine(Path.GetTempPath(), "sntp-adept-rt.xml");
        GrandXml.SaveUnsignedXml(tmp, doc);
        try
        {
            var back = GrandXml.LoadFile(tmp);
            Assert.Equal(6930m, back.Estimates[0].Positions[0].EngineAmount);
            Assert.Equal(894180m, back.Estimates[0].Positions[1].EngineAmount);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
