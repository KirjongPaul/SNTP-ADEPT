using System.Text.Json.Serialization;
using Sntp.Adept.Domain.Books;

namespace Sntp.Adept.Domain.Models;

public sealed class PirDocument
{
    public string Schema { get; set; } = "pir-ir.v1";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public SourceInfo Source { get; set; } = new();
    public ProjectHeader Project { get; set; } = new();
    public Summary1Ps? Summary { get; set; }
    public List<Estimate> Estimates { get; set; } = [];
    public List<Clarification> Issues { get; set; } = [];
    public GrandDocumentExtras? Grand { get; set; }
    /// <summary>Справочник параметров изысканий проекта. Не показатель a/b сметы НЗ.</summary>
    public SurveySheet? Survey { get; set; }
}

public sealed class SourceInfo
{
    public string? File { get; set; }
    public string? Hash { get; set; }
    public string? Kind { get; set; }
}

public sealed class ProjectHeader
{
    public string ObjectName { get; set; } = "";
    public DesignStage Stage { get; set; } = DesignStage.WorkDoc;
    public string StageLabel { get; set; } = "Рабочая документация";
    public string? Phase { get; set; }
    public string Designer { get; set; } = "";
    public string Customer { get; set; } = "";
    public decimal VatRate { get; set; } = 0.22m;
    public decimal? CurrentIndex { get; set; }
    public string? CurrentIndexCaption { get; set; }
    public string? CurrentIndexLetter { get; set; }
    public RoundMode RoundMode { get; set; } = RoundMode.None;
    public KponPolicy KponPolicy { get; set; } = KponPolicy.AutoKpon;
    public string? PriceLevel { get; set; }
    public string? RegionName { get; set; }
    public string? ZoneName { get; set; }
    /// <summary>Папка объекта на диске. Пусто — объект только в SQL.</summary>
    public string? WorkFolder { get; set; }
    /// <summary>Усложняющие факторы объекта. Включены один раз и действуют во всех сметах, где сборник строки совпал.</summary>
    public List<ProjectCondition> Conditions { get; set; } = [];
}

public sealed class ProjectCondition
{
    public string Id { get; set; } = "";
    public string Alias { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<FactorUse> Uses { get; set; } = [];
}

public sealed class Summary1Ps
{
    public string Form { get; set; } = "1PS";
    public List<SummaryRow> Rows { get; set; } = [];
    public SummaryTotals Totals { get; set; } = new();
    public SummaryTotals? ExcelTotals { get; set; }
    public List<Factor> Factors { get; set; } = [];
    public string? Note { get; set; }
    /// <summary>Не показывать в своде строку стадии, например «Рабочая документация».</summary>
    public bool HideStageHeading { get; set; }
}

public sealed class SummaryRow
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Ref { get; set; }
    public decimal Survey { get; set; }
    public decimal Design { get; set; }
    public decimal Total { get; set; }
    public bool Heading { get; set; }
}

public sealed class SummaryTotals
{
    public decimal Design { get; set; }
    public decimal Vat { get; set; }
    public decimal Grand { get; set; }
    public decimal Survey { get; set; }
}

public sealed class Estimate
{
    public string Id { get; set; } = "";
    public string Sheet { get; set; } = "";
    public EstimateForm Form { get; set; } = EstimateForm.Unknown;
    public string Title { get; set; } = "";
    public string? Number { get; set; }
    /// <summary>Кто составил смету.</summary>
    public string? Author { get; set; }
    public string? ChapterCaption { get; set; }
    /// <summary>0 — заголовок 1, 2; 1 — 1.1; 2 — 1.1.1.</summary>
    public int OutlineDepth { get; set; }
    /// <summary>Строка свода без стоимости: заголовок раздела.</summary>
    public bool Heading { get; set; }
    public List<Position> Positions { get; set; } = [];
    public List<Factor> Factors { get; set; } = [];
    public decimal? ExcelAmount { get; set; }
    public decimal? EngineAmount { get; set; }
    public decimal? OptimizationK { get; set; }
    public decimal? DistrictK { get; set; }
    public decimal? VolumeK { get; set; }
    public string? PriceLevel { get; set; }
    /// <summary>Форма 3П МДС (себестоимость / рентабельность). Не A+B·X.</summary>
    public LaborModel? Labor { get; set; }
    /// <summary>Карточка СБЦ АСУТП 2016: части ТЗ–ПО и цепочка коэффициентов.</summary>
    public AsutpModel? Asutp { get; set; }
    /// <summary>Показатели сметы, из которых подбирается расценка.</summary>
    public List<WorkVolume> Volumes { get; set; } = [];
    /// <summary>Усложняющие факторы, включённые только в эту смету.</summary>
    public List<ProjectCondition> Conditions { get; set; } = [];
    /// <summary>Факторы проекта, которые в этой смете не применяются.</summary>
    public List<string> SuppressedConditionIds { get; set; } = [];
}

/// <summary>Раздел 2 формы 3П: з/п → себестоимость / доля → прибыль → районный К.</summary>
public sealed class LaborModel
{
    /// <summary>Типовая группа должностей 3П.</summary>
    public string? GroupCatalogId { get; set; }
    /// <summary>Справочник дневных ставок 3П. Пусто — ставка вводится вручную.</summary>
    public string? WageCatalogId { get; set; }
    /// <summary>Справочник нормативов трудозатрат 3П. Пусто — чел-дни вводятся вручную.</summary>
    public string? HoursCatalogId { get; set; }
    /// <summary>Типовой перечень работ 3П. Задаёт наименование строки.</summary>
    public string? WorkCatalogId { get; set; }
    public decimal SalaryShare { get; set; } = 0.45m;
    public decimal ProfitPercent { get; set; } = 8m;
    public decimal RegionalK { get; set; } = 1m;
    public decimal? SalaryTotal { get; set; }
    public decimal? PrimeCost { get; set; }
    public decimal? Profit { get; set; }
}

/// <summary>СБЦ АСУТП 2016: баллы × ценностный множитель × ∏Ki × 1000, затем понижающий К к итогу.</summary>
public sealed class AsutpModel
{
    public bool NeedTz { get; set; }
    public List<string> EnabledParts { get; set; } = [];
    public decimal? GazpromK { get; set; }
    public decimal CorrectionK { get; set; } = 1m;
    public string? Catalog { get; set; } = "СБЦ АСУТП 2016";
}

public sealed class Position
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Number { get; set; } = "";
    public string Name { get; set; } = "";
    public PositionKind Kind { get; set; } = PositionKind.Work;
    public Basis? Basis { get; set; }
    public string? Code { get; set; }
    public string? Units { get; set; }
    public string? EsnTable { get; set; }
    public decimal? PriceA { get; set; }
    public decimal? PriceB { get; set; }
    public decimal? Quantity { get; set; }
    public string? QuantityUnit { get; set; }
    public decimal Qty { get; set; } = 1m;
    public decimal? Xmin { get; set; }
    public decimal? Xmax { get; set; }
    public List<IntervalItem> Items { get; set; } = [];
    public string? Formula { get; set; }
    public string? ExcelFormula { get; set; }
    public string? ExpandedFormula { get; set; }
    public string? EngineExpandedFormula { get; set; }
    public string? CatalogCode { get; set; }
    public List<Factor> Factors { get; set; } = [];
    public List<RelativeFactor> RelativeFactors { get; set; } = [];
    public decimal? RelativeFactorsProjectDoc { get; set; }
    public decimal? RelativeFactorsWorkDoc { get; set; }
    public decimal? ExcelAmount { get; set; }
    public decimal? EngineAmount { get; set; }
    public decimal? Delta { get; set; }
    public string? SheetName { get; set; }
    public string? EstimateNumber { get; set; }
    public OriginKind Origin { get; set; } = OriginKind.Created;
    public string? SysId { get; set; }
    public string? Vr { get; set; }
    public string? PriceLevel { get; set; }
    public bool HasDbFactors { get; set; }
    public string? Section { get; set; }
    /// <summary>Факторы проекта и сметы, которые на эту расценку не распространяются.</summary>
    public List<string> SuppressedConditionIds { get; set; } = [];
}

public sealed class Basis
{
    public string Kind { get; set; } = "";
    public string? Doc { get; set; }
    public int? Year { get; set; }
    public string? Table { get; set; }
    public string? Item { get; set; }
    public string Raw { get; set; } = "";
    public string? CanonicalDoc { get; set; }
    public int? CollectionNum { get; set; }
    public string? Part { get; set; }
    public string? Chapter { get; set; }
    public string? AdeptCode { get; set; }
    public string? ParsePattern { get; set; }
    public string? MatchStatus { get; set; }
}

public sealed class IntervalItem
{
    public string? Caption { get; set; }
    public string? Code { get; set; }
    public string? EsnTable { get; set; }
    public decimal? MinQuantity { get; set; }
    public decimal? MaxQuantity { get; set; }
    public decimal? PriceA { get; set; }
    public decimal? PriceB { get; set; }
}

public sealed class Factor
{
    public string Identifier { get; set; } = "";
    public string Caption { get; set; } = "";
    public string? Code { get; set; }
    public decimal? ProjectDoc { get; set; }
    public decimal? WorkDoc { get; set; }
    public decimal? WorkProject { get; set; }
    public FactorCombineType CombineType { get; set; } = FactorCombineType.Pricing;
    public string Role { get; set; } = "other";
    public int Level { get; set; }
    public string? Options { get; set; }
    public bool Enabled { get; set; } = true;
    public FactorSource Source { get; set; } = FactorSource.Unknown;
    public string? CatalogKey { get; set; }
    public string? LetterRef { get; set; }
}

public sealed class RelativeFactor
{
    public string Caption { get; set; } = "";
    public decimal? ProjectDoc { get; set; }
    public decimal? WorkDoc { get; set; }
    public decimal? WorkProject { get; set; }
    public decimal? RelativePrice { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Clarification
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public ClarificationSeverity Severity { get; set; }
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public string? EstimateId { get; set; }
    public string? PositionId { get; set; }
    public string? Field { get; set; }
    public string? SuggestedAction { get; set; }
}

public sealed class GrandDocumentExtras
{
    public string DocumentType { get; set; } = GrandGuids.ProjectEstimate;
    public string? ProgramVersion { get; set; }
    public string Generator { get; set; } = "GrandSmeta";
    public string? RegionCode { get; set; }
    public string? MaxSysId { get; set; }
    public string? ItogExpandTo { get; set; }
    public int? InternalCounter { get; set; }
    public string? RawVidRabXml { get; set; }
    public string? RawSignaturesXml { get; set; }
    public string? RawOsInfoXml { get; set; }
    public string? RawRzdXml { get; set; }
    public string? RawAddZatratsXml { get; set; }
    public string? RawParametersXml { get; set; }
    public List<RelativeSummaryItem> RelativeSummary { get; set; } = [];
}

public sealed class RelativeSummaryItem
{
    public string Caption { get; set; } = "";
    public decimal? Value { get; set; }
}

public static class GrandGuids
{
    public const string ProjectEstimate = "{39200715-3435-47E0-8C27-68A028AD6D25}";
    public const string SmrLocal = "{2B0470FD-477C-4359-9F34-EEBE36B7D340}";
}

public static class StageHelper
{
    public static DesignStage FromLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return DesignStage.WorkDoc;
        var t = text.Trim().ToLowerInvariant();
        if (t.Contains("рабоч") || t.Contains("workdoc") || t is "рд" or "р")
            return DesignStage.WorkDoc;
        if (t.Contains("рабочий проект") || t.Contains("workproject") || t is "рп")
            return DesignStage.WorkProject;
        if (t.Contains("проектн") || t.Contains("projectdoc") || t is "пд" or "п")
            return DesignStage.ProjectDoc;
        return DesignStage.WorkDoc;
    }

    public static string ToGrandPhase(DesignStage stage) => stage switch
    {
        DesignStage.WorkDoc => "WorkDoc",
        DesignStage.WorkProject => "WorkProject",
        _ => "ProjectDoc"
    };

    public static DesignStage FromGrandPhase(string? phase) => phase switch
    {
        "WorkDoc" => DesignStage.WorkDoc,
        "WorkProject" => DesignStage.WorkProject,
        _ => DesignStage.ProjectDoc
    };

    public static string ToLabel(DesignStage stage) => stage switch
    {
        DesignStage.WorkDoc => "Рабочая документация",
        DesignStage.WorkProject => "Рабочий проект",
        _ => "Проектная документация"
    };

    public static decimal? Pick(Factor f, DesignStage stage) => stage switch
    {
        DesignStage.WorkDoc => f.WorkDoc ?? f.ProjectDoc,
        DesignStage.WorkProject => f.WorkProject ?? f.WorkDoc ?? f.ProjectDoc,
        _ => f.ProjectDoc ?? f.WorkDoc
    };
}
