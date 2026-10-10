using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Models;
using Sntp.Adept.Excel;

namespace Sntp.Adept.Tests;

public class ExcelParseTests
{
    [Fact]
    public void V32_Svod_Totals_FromCachedFormulas()
    {
        var path = TestPaths.V32Excel();
        var doc = ExcelImporter.Import(path);
        Assert.NotNull(doc.Summary);
        Assert.Equal(111117470.75m, doc.Summary.ExcelTotals!.Design);
        Assert.Equal(24445843.57m, doc.Summary.ExcelTotals.Vat);
        Assert.Equal(135563314.32m, doc.Summary.ExcelTotals.Grand);
        Assert.Equal(0.22m, doc.Project.VatRate);
        Assert.Contains("Степновск", doc.Project.ObjectName);
        Assert.Equal(DesignStage.WorkDoc, doc.Project.Stage);
        Assert.Contains("СИБНЕФТЕТРАНСПРОЕКТ", doc.Project.Designer);
        Assert.Equal(doc.Estimates.Count, doc.Summary.Rows.Count);
        Assert.Equal(doc.Estimates[0].Number, doc.Summary.Rows[0].Code);
        Assert.DoesNotContain(doc.Summary.Rows, r => r.Title.Contains("Итого", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void V32_Sheet11_Position1_FromRoundFormula()
    {
        var sheets = SheetTable.LoadWorkbook(TestPaths.V32Excel());
        var s11 = sheets.First(x => x.Name.Contains("1.1"));
        var formula = s11.Formula(14, 14);
        Assert.False(string.IsNullOrWhiteSpace(formula));
        Assert.Contains("1.56", formula);
        var parsed = ExcelRoundFormula.Parse(formula);
        Assert.Equal(31100m, parsed!.PriceA);
        Assert.Equal(122993m, parsed.PriceB);
        Assert.Equal(5m, parsed.Quantity);
        Assert.Equal(604716.84m, parsed.Evaluated);
        Assert.Equal(604716.84m, Math.Round(s11.Number(14, 14)!.Value, 2, MidpointRounding.AwayFromZero));

        var doc = ExcelImporter.Import(TestPaths.V32Excel());
        var est = doc.Estimates.First(e => e.Number == "1.1" || e.Sheet.Contains("1.1"));
        Assert.Equal(EstimateForm.Form2P707, est.Form);
        var p1 = est.Positions.First(p => p.Kind == PositionKind.Work && p.Number == "1");
        Assert.Equal(31100m, p1.PriceA);
        Assert.Equal(122993m, p1.PriceB);
        Assert.Equal(5m, p1.Quantity);
        Assert.Contains(p1.Factors, f => f.Identifier == "Ктек" && f.WorkDoc == 1.56m);
        Assert.Contains(p1.Factors, f => f.Identifier == "Кст" && f.WorkDoc == 0.6m);
        Assert.NotNull(p1.ExcelFormula);
        Assert.Contains("ROUND", p1.ExcelFormula!, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(p1.ExcelAmount);
        Assert.True(Math.Abs(p1.ExcelAmount!.Value - 604716.84m) < 0.02m);
        Assert.True(p1.EngineAmount is decimal eng && Math.Abs(eng - 604716.84m) < 1m);
        Assert.Equal("NZ-849-3.8-10", p1.CatalogCode);
        Assert.Equal(1.56m, doc.Project.CurrentIndex);
    }

    [Fact]
    public void V32_Dialects_SixteenSheets()
    {
        var sheets = SheetTable.LoadWorkbook(TestPaths.V32Excel());
        Assert.Equal(16, sheets.Count);
        var forms = sheets.Select(DialectDetector.Detect).ToList();
        Assert.Equal(EstimateForm.Form1Ps, forms[0]);
        Assert.Equal(6, forms.Count(f => f == EstimateForm.Form2P707));
        Assert.Equal(4, forms.Count(f => f == EstimateForm.AsutpCard));
        Assert.Equal(4, forms.Count(f => f == EstimateForm.Form2PSbc));
        Assert.Equal(1, forms.Count(f => f == EstimateForm.Form3P));
        Assert.Equal(0, forms.Count(f => f == EstimateForm.LaborCost));
    }

    [Fact]
    public void V32_Import_ReportsRealSheetProgress()
    {
        var events = new List<ImportProgress>();
        var doc = ExcelImporter.Import(TestPaths.V32Excel(), events.Add);

        Assert.Contains("Степновск", doc.Project.ObjectName);
        Assert.True(events.Count >= 18, $"ожидались шаги open/16 листов/recalc, получено {events.Count}");
        Assert.Equal("open", events[0].Phase);
        Assert.Contains(events, p => p.Phase == "sheet" && p.Form == "свод 1ПС");
        Assert.Contains(events, p => p.Phase == "sheet" && p.Form == "позиции 2П");
        Assert.Contains(events, p => p.Phase == "sheet" && p.Form == "карточка АСУТП");
        Assert.Contains(events, p => p.Phase == "sheet" && p.Form == "позиции 2П СБЦ");
        Assert.Contains(events, p => p.Phase == "sheet" && p.Form == "форма 3П");
        Assert.Contains(events, p => p.Sheet != null && p.Sheet.Contains("1.1", StringComparison.Ordinal));
        var sheetNames = events
            .Where(p => p.Phase == "sheet" && !string.IsNullOrWhiteSpace(p.Sheet))
            .Select(p => p.Sheet!)
            .Distinct()
            .ToList();
        Assert.Equal(16, sheetNames.Count);
        Assert.Equal(16, events.Last(p => p.Phase == "sheet").SheetCount);
        Assert.Contains(events, p => p.Phase == "recalc");
        Assert.True(events[^1].Percent >= 88);
        for (var i = 1; i < events.Count; i++)
            Assert.True(events[i].Percent >= events[i - 1].Percent,
                $"процент упал {events[i - 1].Percent} → {events[i].Percent} на «{events[i].Message}»");
    }

    [Fact]
    public void ImportProgress_FormLabels()
    {
        Assert.Equal("свод 1ПС", ImportProgress.FormLabel(EstimateForm.Form1Ps));
        Assert.Equal("позиции 2П", ImportProgress.FormLabel(EstimateForm.Form2P707));
        Assert.Equal("позиции 2П СБЦ", ImportProgress.FormLabel(EstimateForm.Form2PSbc));
        Assert.Equal("карточка АСУТП", ImportProgress.FormLabel(EstimateForm.AsutpCard));
        Assert.Equal("форма 3П", ImportProgress.FormLabel(EstimateForm.Form3P));
        Assert.Equal("форма 3П", ImportProgress.FormLabel(EstimateForm.LaborCost));
    }

    [Fact]
    public void V32_Form3P_114_RolesAndTotals()
    {
        var doc = ExcelImporter.Import(TestPaths.V32Excel());
        var est = doc.Estimates.First(e => e.Number == "1.14" || e.Sheet.Contains("1.14", StringComparison.Ordinal));
        Assert.Equal(EstimateForm.Form3P, est.Form);
        Assert.NotNull(est.Labor);
        Assert.Equal(0.45m, est.Labor!.SalaryShare);
        Assert.Equal(8m, est.Labor.ProfitPercent);
        Assert.Equal(1m, est.Labor.RegionalK);
        Assert.True(est.Positions.Count >= 7, $"ожидались исполнители, получено {est.Positions.Count}");
        Assert.Contains(est.Positions, p => p.Name.Contains("ГИП", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(526369.20m, est.EngineAmount);
        Assert.True(est.ExcelAmount is decimal x && Math.Abs(x - 526369.20m) < 0.02m);
        Assert.Equal(219320.50m, est.Labor.SalaryTotal);
    }

    [Fact]
    public void V32_Asutp_15_PartsAndCorrection()
    {
        var doc = ExcelImporter.Import(TestPaths.V32Excel());
        var est = doc.Estimates.First(e => e.Number == "1.5" || e.Sheet.Contains("1.5", StringComparison.Ordinal));
        Assert.Equal(EstimateForm.AsutpCard, est.Form);
        Assert.NotNull(est.Asutp);
        Assert.Contains("ОР", est.Asutp!.EnabledParts);
        Assert.Contains("ТО", est.Asutp.EnabledParts);
        Assert.Equal(0.7m, est.Asutp.CorrectionK);
        Assert.Contains(est.Positions, p => p.Name == "ОР" && p.Quantity == 36m);
        Assert.Contains(est.Positions, p => p.Name == "ТО" && p.Quantity == 43m);
        var or = est.Positions.First(p => p.Name == "ОР");
        Assert.Contains(or.Factors, f => f.Identifier.Contains("К1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(or.Factors, f => f.Identifier.Contains("Кин", StringComparison.OrdinalIgnoreCase) && f.WorkDoc == 7.22m);
        Assert.Contains(or.Factors, f => f.Identifier.Contains("Кст", StringComparison.OrdinalIgnoreCase) && f.WorkDoc == 0.3m);
        Assert.True(est.EngineAmount is decimal eng && Math.Abs(eng - 3980436m) < 2m,
            $"движок АСУТП 1.5 = {est.EngineAmount}, ожидалось ≈ 3980436");
        Assert.True(est.ExcelAmount is decimal xl && Math.Abs(xl - 3980436m) < 2m,
            $"Excel АСУТП 1.5 = {est.ExcelAmount}");
    }
}
