using Sntp.Adept.Domain.Books;

namespace Sntp.Adept.Tests;

public class SurveyGeologyTests
{
    [Fact]
    public void Gas_line_10km_uses_table_7_2_range()
    {
        var sheet = SurveyGeology.Compose(new SurveyInputs { LengthM = 10000 });
        var spacing = Row(sheet, "spacing");
        var intervals = Row(sheet, "intervals");

        Assert.Equal(100m, spacing.Quantity);
        Assert.Equal(300m, spacing.QuantityTo);
        Assert.Equal("7.1.11", spacing.NormClause);
        Assert.Equal("7.2", spacing.NormTable);
        Assert.Equal("561027906", spacing.NormNd);
        Assert.Contains(spacing.Protocol, line => line.Contains("7.1.11", StringComparison.Ordinal) && line.Contains("таблицей 7.2", StringComparison.Ordinal));
        Assert.Contains(spacing.Protocol, line => line.Contains("100–300", StringComparison.Ordinal));
        Assert.Equal(33.333m, intervals.Quantity);
        Assert.Equal(100m, intervals.QuantityTo);
        Assert.Contains(intervals.Protocol, line => line.Contains("формулы числа скважин", StringComparison.Ordinal));
        Assert.DoesNotContain(sheet.Parameters.SelectMany(p => p.Protocol), line => line.Contains("10.3", StringComparison.Ordinal));
    }

    [Fact]
    public void Depth_is_one_to_two_meters_below_laying_and_frost()
    {
        var sheet = SurveyGeology.Compose(new SurveyInputs
        {
            LengthM = 10000,
            LayingDepthM = 2,
            FrostDepthM = 1.5m,
            SpacingM = 200,
            DiameterMm = 160,
            SoilCategory = "II"
        });
        var depth = Row(sheet, "depth");
        var meters = Row(sheet, "meters");
        var intervals = Row(sheet, "intervals");

        Assert.Equal(3m, depth.Quantity);
        Assert.Equal(4m, depth.QuantityTo);
        Assert.Contains("1–2 м ниже предполагаемой глубины заложения", depth.Quote);
        Assert.Contains("1–2 м ниже нормативной глубины промерзания", depth.Quote);
        Assert.Equal(50m, intervals.Quantity);
        Assert.Null(intervals.QuantityTo);
        Assert.Equal("2", meters.Uses[0].Item);
        Assert.Equal("14", meters.NormTable);
        Assert.Equal("1312801877", meters.NormNd);
        Assert.Equal(150m, meters.Quantity);
        Assert.Equal(200m, meters.QuantityTo);
    }

    [Fact]
    public void Manual_number_keeps_the_clause()
    {
        var first = SurveyGeology.Compose(new SurveyInputs { LengthM = 10000, SpacingM = 200 });
        var intervals = Row(first, "intervals");
        intervals.Manual = true;
        intervals.Quantity = 40;
        intervals.QuantityTo = null;
        var again = SurveyGeology.Compose(SurveyGeology.ReadInputs(first), first.Parameters);
        var edited = Row(again, "intervals");

        Assert.True(edited.Manual);
        Assert.Equal(40m, edited.Quantity);
        Assert.Contains(edited.Protocol, line => line.Contains("задано вручную", StringComparison.Ordinal));
        Assert.Contains(edited.Protocol, line => line.Contains("7.1.11", StringComparison.Ordinal));
        Assert.Equal("7.1.11", edited.NormClause);
    }

    private static SurveyParameter Row(SurveySheet sheet, string id) =>
        sheet.Parameters.Single(p => p.Id == id);
}
