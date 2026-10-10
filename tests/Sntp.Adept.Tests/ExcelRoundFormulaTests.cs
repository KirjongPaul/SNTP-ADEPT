using Sntp.Adept.Domain.Calc;

namespace Sntp.Adept.Tests;

public class ExcelRoundFormulaTests
{
    [Fact]
    public void EnglishRound_V32_Position1()
    {
        var p = ExcelRoundFormula.Parse("=ROUND((31100 + 122993 * 5) * 1 * 1.56 * 0.6, 2)");
        Assert.NotNull(p);
        Assert.Equal(31100m, p!.PriceA);
        Assert.Equal(122993m, p.PriceB);
        Assert.Equal(5m, p.Quantity);
        Assert.Equal(1m, p.Qty);
        Assert.Equal(604716.84m, p.Evaluated);
        Assert.Contains(1.56m, p.Multipliers);
        Assert.Contains(0.6m, p.Multipliers);
    }

    [Fact]
    public void RussianRound_CommaDecimal_AndSemicolon()
    {
        var p = ExcelRoundFormula.Parse("=ОКРУГЛ((31100 + 122993 * 5) * 1 * 1,56 * 0.6; 2)");
        Assert.NotNull(p);
        Assert.Equal(31100m, p!.PriceA);
        Assert.Equal(1.56m, p.Multipliers.First(x => x > 1));
        Assert.Equal(604716.84m, p.Evaluated);
    }

    [Fact]
    public void BelowMinExtrapolation_Tokens()
    {
        var p = ExcelRoundFormula.Parse("=ROUND((31100 + 122993 * (0.4 * 0.5 + 0.6 * 0.4)) * 1 * 1.56 * 0.6, 2)");
        Assert.NotNull(p);
        Assert.True(p!.BelowMinExtrapolation);
        Assert.Equal(0.5m, p.Xmin);
        Assert.Equal(0.4m, p.Quantity);
        Assert.Equal(79763.04m, p.Evaluated);
    }

    [Fact]
    public void SbcProduct_DoesNotTreatStageAsQty()
    {
        var p = ExcelRoundFormula.Parse("=2304*0.6*55.26*1.5*1.2*1.1*1.3");
        Assert.NotNull(p);
        Assert.Equal(2304m, p!.PriceA);
        Assert.Equal(0m, p.PriceB);
        Assert.Null(p.Qty);
        Assert.Equal(0.6m, p.Multipliers[0]);
    }
}
