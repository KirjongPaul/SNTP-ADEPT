using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

public sealed class CreateObjectRequest
{
    public string ObjectName { get; set; } = "Новый объект";
    public DesignStage Stage { get; set; } = DesignStage.WorkDoc;
    public string? Phase { get; set; }
    /// <summary>Корень, внутри которого создаётся рабочая папка объекта.</summary>
    public string? WorkRoot { get; set; }
    public string Designer { get; set; } = "";
    public string Customer { get; set; } = "";
    public decimal VatRate { get; set; } = 0.22m;
    public decimal? CurrentIndex { get; set; }
    public RoundMode RoundMode { get; set; } = RoundMode.None;
    public KponPolicy KponPolicy { get; set; } = KponPolicy.AutoKpon;
}

public sealed class CreateLocalRequest
{
    public string? Title { get; set; }
    public string? Number { get; set; }
    public EstimateForm Form { get; set; } = EstimateForm.Form2P707;
    public string? ChapterCaption { get; set; }
}

public static class DocumentFactory
{
    public static Factor DefaultKi1() => new()
    {
        Identifier = "Ки1",
        Caption = "Стадийность проектирования",
        ProjectDoc = 0.4m,
        WorkDoc = 0.6m,
        WorkProject = 1m,
        Level = 11,
        Role = "stage",
        Options = "UseInPos AllChapters AllVidRabs",
        CombineType = FactorCombineType.Pricing,
        Enabled = true
    };

    public static PirDocument CreateObject(CreateObjectRequest req)
    {
        var stage = req.Stage;
        var doc = new PirDocument
        {
            Source = new SourceInfo { Kind = "created" },
            Project = new ProjectHeader
            {
                ObjectName = string.IsNullOrWhiteSpace(req.ObjectName) ? "Новый объект" : req.ObjectName.Trim(),
                Stage = stage,
                StageLabel = StageHelper.ToLabel(stage),
                Phase = req.Phase,
                Designer = req.Designer,
                Customer = req.Customer,
                VatRate = req.VatRate,
                CurrentIndex = req.CurrentIndex,
                RoundMode = req.RoundMode,
                KponPolicy = req.KponPolicy,
                RegionName = "Проектно-изыскательские работы",
                ZoneName = "Базовый федеральный район"
            },
            Grand = new GrandDocumentExtras
            {
                DocumentType = GrandGuids.ProjectEstimate,
                Generator = "GrandSmeta",
                ProgramVersion = "16.2.1.59411"
            }
        };
        RebuildSummary(doc);
        return doc;
    }

    public static Estimate CreateLocal(PirDocument doc, CreateLocalRequest req)
    {
        var n = NextLocalNumber(doc, req.Number);
        var title = string.IsNullOrWhiteSpace(req.Title) ? $"Локальная смета {n}" : req.Title.Trim();
        var est = new Estimate
        {
            Id = n,
            Number = n,
            Title = title,
            Sheet = title,
            Form = req.Form == EstimateForm.Unknown ? EstimateForm.Form2P707 : req.Form,
            ChapterCaption = string.IsNullOrWhiteSpace(req.ChapterCaption) ? "Новый раздел" : req.ChapterCaption,
            Factors = req.Form is EstimateForm.Form3P or EstimateForm.LaborCost or EstimateForm.AsutpCard
                ? []
                : [DefaultKi1()],
            Labor = req.Form is EstimateForm.Form3P or EstimateForm.LaborCost ? new LaborModel() : null,
            Asutp = req.Form == EstimateForm.AsutpCard ? new AsutpModel() : null,
            PriceLevel = doc.Project.PriceLevel
        };
        if (doc.Project.CurrentIndex is decimal ktek && ktek != 1m)
        {
            est.Factors.Add(new Factor
            {
                Identifier = "Ктек",
                Caption = "Индекс к текущему уровню",
                Role = "index",
                ProjectDoc = ktek,
                WorkDoc = ktek,
                WorkProject = ktek,
                CombineType = FactorCombineType.Pricing
            });
        }

        doc.Estimates.Add(est);
        EstimateOutline.Renumber(doc);
        RebuildSummary(doc);
        return est;
    }

    public static Estimate CreateHeading(PirDocument doc, string? title, string? beforeId)
    {
        var est = new Estimate
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Title = string.IsNullOrWhiteSpace(title) ? "Новый раздел" : title.Trim(),
            Heading = true,
            OutlineDepth = 0,
            Form = EstimateForm.Unknown
        };
        var at = string.IsNullOrWhiteSpace(beforeId)
            ? doc.Estimates.Count
            : doc.Estimates.FindIndex(e => e.Id == beforeId || e.Number == beforeId);
        if (at < 0) at = doc.Estimates.Count;
        if (at < doc.Estimates.Count)
            est.OutlineDepth = doc.Estimates[at].OutlineDepth;
        doc.Estimates.Insert(at, est);
        EstimateOutline.Renumber(doc);
        RebuildSummary(doc);
        return est;
    }

    public static Estimate CreateForm3P(PirDocument doc)
    {
        var est = CreateLocal(doc, new CreateLocalRequest
        {
            Title = "Трудозатраты",
            Form = EstimateForm.Form3P,
            ChapterCaption = "Форма 3П"
        });
        if (est.Labor is not null)
        {
            est.Labor.GroupCatalogId = "sntp-groups";
            est.Labor.WageCatalogId = "sntp-20-26";
            est.Labor.HoursCatalogId = "sibur-hours";
            est.Labor.WorkCatalogId = "sibur-works";
        }
        est.OutlineDepth = 0;
        EstimateOutline.Renumber(doc);
        RebuildSummary(doc);
        return est;
    }

    public static Estimate CreateAutomation(PirDocument doc)
    {
        var parts = new[] { "ТЗ", "ОР", "ОО", "ИО", "ТО", "МО", "ПО" };
        var est = new Estimate
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Title = "Автоматизация",
            Form = EstimateForm.AsutpCard,
            OutlineDepth = 0,
            ChapterCaption = "АСУТП",
            Asutp = new AsutpModel
            {
                Catalog = "СБЦП 81-2001-22",
                NeedTz = true,
                EnabledParts = [..parts],
                CorrectionK = 1m
            },
            Factors =
            [
                DefaultKi1(),
                new Factor
                {
                    Identifier = "К1",
                    Caption = "Доля повторно используемых решений",
                    Role = "other",
                    ProjectDoc = 1m,
                    WorkDoc = 1m,
                    WorkProject = 1m,
                    CombineType = FactorCombineType.Pricing
                },
                new Factor
                {
                    Identifier = "Крк",
                    Caption = "Районный коэффициент",
                    Role = "district",
                    ProjectDoc = 1m,
                    WorkDoc = 1m,
                    WorkProject = 1m,
                    CombineType = FactorCombineType.Pricing
                }
            ]
        };
        if (doc.Project.CurrentIndex is decimal ktek)
        {
            est.Factors.Add(new Factor
            {
                Identifier = "Ктек",
                Caption = "Индекс к текущему уровню",
                Role = "index",
                ProjectDoc = ktek,
                WorkDoc = ktek,
                WorkProject = ktek,
                CombineType = FactorCombineType.Pricing
            });
        }

        foreach (var part in parts)
        {
            est.Positions.Add(new Position
            {
                Name = part,
                Kind = PositionKind.Work,
                Section = part,
                Quantity = 0m,
                PriceA = 0m,
                Qty = 1m,
                Origin = OriginKind.Created,
                Basis = BasisParser.Parse("СБЦ АСУТП 2016"),
                CatalogCode = "SBC-81-2001-22"
            });
        }

        doc.Estimates.Add(est);
        EstimateOutline.Renumber(doc);
        RebuildSummary(doc);
        return est;
    }

    public static Position CreatePosition(Estimate est, string? number = null)
    {
        var no = number ?? NextPositionNumber(est);
        var pos = new Position
        {
            Number = no,
            Name = "Новая позиция",
            Kind = PositionKind.Work,
            Qty = 1m,
            Origin = OriginKind.Created,
            EstimateNumber = est.Number,
            SheetName = est.Sheet,
            Section = est.ChapterCaption ?? "Новый раздел"
        };
        est.Positions.Add(pos);
        return pos;
    }

    public static Summary1Ps RebuildSummary(PirDocument doc)
    {
        CalcEngine.RebuildSummary(doc);
        return doc.Summary!;
    }

    public static string NextLocalNumber(PirDocument doc, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested) &&
            doc.Estimates.All(e => !string.Equals(e.Number, requested, StringComparison.OrdinalIgnoreCase)
                                   && !string.Equals(e.Id, requested, StringComparison.OrdinalIgnoreCase)))
            return requested.Trim();

        var i = doc.Estimates.Count + 1;
        while (doc.Estimates.Any(e => e.Number == i.ToString() || e.Id == i.ToString()))
            i++;
        return i.ToString();
    }

    private static string NextPositionNumber(Estimate est)
    {
        var max = 0;
        foreach (var p in est.Positions)
        {
            if (int.TryParse(p.Number, out var n) && n > max)
                max = n;
        }
        return (max + 1).ToString();
    }
}
