namespace Sntp.Adept.Domain.Models;

/// <summary>Стадия П / РД / РП (Grand ProjectPhase).</summary>
public enum DesignStage
{
    ProjectDoc,
    WorkDoc,
    WorkProject
}

public enum EstimateForm
{
    Form2P707,
    Form2PSbc,
    Form2PSurveyNz,
    Form2PSurveySbc,
    Form3P,
    Form4P,
    AsutpCard,
    Form1Ps,
    LaborCost,
    Unknown
}

public enum PositionKind
{
    Work,
    Accrual,
    Total,
    Section,
    SectionTotal,
    Text
}

/// <summary>Типы сочетания коэффициентов Адепт (в XML Гранда сворачиваются в множители).</summary>
public enum FactorCombineType
{
    Pricing,
    Complicating,
    Composite,
    Divisor
}

/// <summary>Откуда взято значение коэффициента.</summary>
public enum FactorSource
{
    Unknown,
    Formula,
    Text,
    Both,
    Catalog,
    Index,
    Manual
}

public enum RoundMode
{
    None,
    Round1,
    Round10
}

public enum KponPolicy
{
    AutoKpon,
    RequireClarification,
    Expert
}

public enum ClarificationSeverity
{
    Info,
    Warning,
    Error
}

public enum OriginKind
{
    Excel,
    GrandXml,
    Created
}
