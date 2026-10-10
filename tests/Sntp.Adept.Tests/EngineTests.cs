using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class EngineTests
{
    [Fact]
    public void Position1_GrandSample_Total6930()
    {
        var doc = GrandSampleDoc();
        var pos = doc.Estimates[0].Positions[0];
        var r = CalcEngine.CalculatePosition(pos, doc, doc.Estimates[0]);
        Assert.Equal(6930m, r.Total);
        Assert.Equal("above-max", r.Mode);
        Assert.Contains("0.4*60+0.6*1000", r.ExpandedFormula);
        Assert.Contains(r.Issues, i => i.Code == "x-above-2max");
    }

    [Fact]
    public void Position2_GrandSample_Total894180()
    {
        var doc = GrandSampleDoc();
        var pos = doc.Estimates[0].Positions[1];
        var r = CalcEngine.CalculatePosition(pos, doc, doc.Estimates[0]);
        Assert.Equal(894180m, r.Total);
        Assert.Equal("above-max", r.Mode);
        Assert.Equal(294250m, r.SelectedItem?.PriceA);
        Assert.Equal(2110m, r.SelectedItem?.PriceB);
    }

    [Fact]
    public void InRange_Uses_A_plus_BX()
    {
        var doc = EmptyRd();
        var est = doc.Estimates[0];
        var pos = new Position
        {
            PriceA = 31100m,
            PriceB = 122993m,
            Quantity = 5m,
            Xmin = 0.5m,
            Xmax = 10m,
            Factors =
            [
                Factor("Ктек", 1.53m, "index"),
                Factor("Кст", 0.6m, "stage")
            ]
        };
        var r = CalcEngine.CalculatePosition(pos, doc, est);
        Assert.Equal("in-range", r.Mode);
        Assert.Equal(593087.67m, Math.Round(r.Total, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void BelowXmin_Extrapolates()
    {
        var doc = EmptyRd();
        var pos = new Position
        {
            PriceA = 1000m,
            PriceB = 10m,
            Quantity = 8m,
            Xmin = 10m,
            Xmax = 20m
        };
        var r = CalcEngine.CalculatePosition(pos, doc, doc.Estimates[0]);
        Assert.Equal("below-min", r.Mode);
        Assert.Equal(1000m + 10m * (0.4m * 10m + 0.6m * 8m), r.BaseCost);
    }

    [Fact]
    public void Kpon_SmallObject_MatchesExcel13p7()
    {
        var doc = EmptyRd();
        doc.Project.KponPolicy = KponPolicy.AutoKpon;
        var pos = new Position
        {
            PriceA = 467700m,
            PriceB = 398m,
            Quantity = 10m,
            Xmin = 50m,
            Xmax = 200m,
            Factors =
            [
                Factor("Ктек", 1.53m, "index"),
                Factor("Кст", 0.6m, "stage")
            ]
        };
        var r = CalcEngine.CalculatePosition(pos, doc, doc.Estimates[0]);
        Assert.Equal("kpon-small", r.Mode);
        Assert.Equal(0.4m, r.Kpon);
        Assert.Equal(176854.54m, Math.Round(r.Total, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void CreateObject_And_EmptyLocal_HaveKi1()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest
        {
            ObjectName = "Тест",
            Stage = DesignStage.WorkDoc,
            VatRate = 0.22m
        });
        Assert.Empty(doc.Estimates);
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "ГП", Number = "1.1" });
        Assert.Single(doc.Estimates);
        Assert.Contains(est.Factors, f => f.Identifier == "Ки1" && f.ProjectDoc == 0.4m && f.WorkDoc == 0.6m && f.WorkProject == 1m);
        Assert.Equal("Новый раздел", est.ChapterCaption);
        var pos = DocumentFactory.CreatePosition(est);
        pos.PriceA = 1000;
        pos.PriceB = 0;
        pos.Quantity = 1;
        CalcEngine.Recalculate(doc);
        Assert.Equal(600m, pos.EngineAmount);
        Assert.NotNull(doc.Summary);
        Assert.Single(doc.Summary!.Rows);
    }

    [Fact]
    public void RebuildSummary_AppliesVat()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { VatRate = 0.22m, ObjectName = "X" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "A" });
        est.EngineAmount = 1000m;
        DocumentFactory.RebuildSummary(doc);
        Assert.Equal(1000m, doc.Summary!.Totals.Design);
        Assert.Equal(220m, doc.Summary.Totals.Vat);
        Assert.Equal(1220m, doc.Summary.Totals.Grand);
    }

    [Fact]
    public void Project_Condition_Adds_Complicating_Factor_Only_For_Its_Book()
    {
        var doc = EmptyRd();
        doc.Project.Conditions.Add(new ProjectCondition
        {
            Id = "hydrogenSulfide",
            Alias = "Сероводород",
            Uses =
            [
                new Sntp.Adept.Domain.Books.FactorUse
                {
                    BookId = "nz-849-2023",
                    BookMark = "849",
                    Table = "2.1",
                    Item = "2",
                    Value = 1.2m,
                    PriceTables = ["3.8", "3.1"]
                }
            ]
        });
        var hit = new Position
        {
            PriceA = 10000m,
            PriceB = 0m,
            Quantity = 1m,
            Basis = new Basis { Table = "3.8", Raw = "НЗ 849/пр табл. 3.8" }
        };
        var miss = new Position
        {
            PriceA = 10000m,
            PriceB = 0m,
            Quantity = 1m,
            Basis = new Basis { Table = "3.8", Raw = "СБЦ 1.18" }
        };
        var est = doc.Estimates[0];
        var hitResult = CalcEngine.CalculatePosition(hit, doc, est);
        Assert.Equal(12000m, hitResult.Total);
        Assert.Contains("hydrogenSulfide", hitResult.SymbolicFormula);
        Assert.Equal(10000m, CalcEngine.CalculatePosition(miss, doc, est).Total);
        est.SuppressedConditionIds.Add("hydrogenSulfide");
        Assert.Equal(10000m, CalcEngine.CalculatePosition(hit, doc, est).Total);
        est.SuppressedConditionIds.Clear();
        hit.SuppressedConditionIds.Add("hydrogenSulfide");
        Assert.Equal(10000m, CalcEngine.CalculatePosition(hit, doc, est).Total);
    }

    private static PirDocument EmptyRd()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { Stage = DesignStage.WorkDoc });
        DocumentFactory.CreateLocal(doc, new CreateLocalRequest());
        doc.Estimates[0].Factors.Clear();
        return doc;
    }

    private static Factor Factor(string id, decimal v, string role) => new()
    {
        Identifier = id,
        Role = role,
        ProjectDoc = v,
        WorkDoc = v,
        WorkProject = v
    };

    private static PirDocument GrandSampleDoc()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest
        {
            ObjectName = "Проектная смета",
            Stage = DesignStage.ProjectDoc,
            RoundMode = RoundMode.Round10
        });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "XML" });
        est.Factors = [DocumentFactory.DefaultKi1()];

        est.Positions.Add(new Position
        {
            Number = "1",
            Code = "СБЦП19-5-1",
            PriceA = 8640,
            PriceB = 17,
            Quantity = 1000,
            Xmin = 20,
            Xmax = 60,
            Items =
            [
                new IntervalItem { MinQuantity = 20, MaxQuantity = 60, PriceA = 8640, PriceB = 17, Code = "СБЦП19-5-1" }
            ],
            Factors =
            [
                new Factor
                {
                    Identifier = "К1",
                    ProjectDoc = 0.9m,
                    WorkDoc = 0.9m,
                    WorkProject = 0.9m
                }
            ]
        });

        est.Positions.Add(new Position
        {
            Number = "2",
            Code = "СБЦП16-1-1.11-1",
            PriceA = 44500,
            PriceB = 2810,
            Quantity = 1000,
            Items =
            [
                new IntervalItem { Caption = "до 25", MaxQuantity = 25, PriceA = 44500, PriceB = 2810 },
                new IntervalItem { Caption = "свыше 800", MinQuantity = 800, PriceA = 294250, PriceB = 2110, Code = "СБЦП16-1-1.11-1" }
            ]
        });

        return doc;
    }
}
