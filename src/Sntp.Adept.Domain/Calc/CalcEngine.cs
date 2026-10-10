using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Calc;

public sealed class PositionCalcResult
{
    public decimal Total { get; init; }
    public decimal BaseCost { get; init; }
    public decimal Xeffective { get; init; }
    public decimal FactorProduct { get; init; }
    public decimal? Kpon { get; init; }
    public string Mode { get; init; } = "in-range";
    public string ExpandedFormula { get; init; } = "";
    public string SymbolicFormula { get; init; } = "";
    public IntervalItem? SelectedItem { get; init; }
    public List<Clarification> Issues { get; init; } = [];
}

public static class CalcEngine
{
    internal static readonly HashSet<string> ImportIssueCodes =
    [
        "asutp-partial", "import", "formula-text-mismatch", "labor-partial",
        "catalog-miss", "unknown-sheet", "coeff-unmatched", "analog-match"
    ];

    public static void Recalculate(PirDocument doc)
    {
        var keep = doc.Issues.Where(i => ImportIssueCodes.Contains(i.Code)).ToList();
        doc.Issues = keep;

        foreach (var est in doc.Estimates)
        {
            PositionOrder.Arrange(est);
            decimal workSum = 0;
            foreach (var pos in est.Positions)
            {
                if (pos.Kind != PositionKind.Work)
                {
                    pos.EngineAmount = pos.Kind == PositionKind.Accrual ? pos.ExcelAmount : pos.EngineAmount;
                    continue;
                }

                var r = CalculatePosition(pos, doc, est);
                pos.EngineAmount = r.Total;
                pos.EngineExpandedFormula = r.ExpandedFormula;
                if (string.IsNullOrWhiteSpace(pos.Formula))
                    pos.Formula = r.SymbolicFormula;
                pos.Delta = pos.ExcelAmount is decimal excel
                    ? Math.Round(r.Total - excel, 4)
                    : null;
                foreach (var issue in r.Issues)
                {
                    issue.EstimateId = est.Id;
                    issue.PositionId = pos.Id;
                    doc.Issues.Add(issue);
                }

                UpdateRelativePrices(pos, r.Total, doc.Project.Stage);
                workSum += r.Total;

                if (pos.ExcelAmount is decimal ex && Math.Abs(r.Total - ex) > 1m)
                {
                    doc.Issues.Add(new Clarification
                    {
                        Severity = ClarificationSeverity.Warning,
                        Code = "excel-delta",
                        Message = $"Расхождение движка и Excel: {r.Total:0.##} vs {ex:0.##} ₽",
                        EstimateId = est.Id,
                        PositionId = pos.Id,
                        Field = "amount",
                        SuggestedAction = "Сверить A/B/X и коэффициенты"
                    });
                }
            }

            if (IsLaborForm(est.Form))
                ApplyLaborTotals(est);
            else if (est.Form == EstimateForm.AsutpCard)
            {
                var corr = est.Asutp?.CorrectionK ?? 1m;
                est.EngineAmount = Math.Round(workSum * corr, 0, MidpointRounding.AwayFromZero);
            }
            else
            {
                var k = (est.OptimizationK ?? 1m) * (est.DistrictK ?? 1m) * (est.VolumeK ?? 1m);
                est.EngineAmount = NumberParse.RoundMoney(workSum * k, doc.Project.RoundMode);
            }

            PositionOrder.FillTotals(est);
        }

        if (doc.Summary is not null)
            RebuildSummary(doc);
    }

    public static PositionCalcResult CalculatePosition(Position pos, PirDocument doc, Estimate? estimate = null)
    {
        if (estimate is not null && IsLaborForm(estimate.Form))
            return CalculateLaborLine(pos);
        if (estimate?.Form == EstimateForm.AsutpCard)
            return CalculateAsutpPart(pos, doc, estimate);

        var issues = new List<Clarification>();
        var selected = SelectItem(pos.Items, pos.Quantity);
        var a = selected?.PriceA ?? pos.PriceA ?? 0m;
        var b = selected?.PriceB ?? pos.PriceB ?? 0m;
        var x = pos.Quantity ?? 0m;
        var xmin = selected?.MinQuantity ?? pos.Xmin;
        var xmax = EffectiveXmax(selected, pos);

        if (pos.Quantity is null)
        {
            issues.Add(new Clarification
            {
                Severity = ClarificationSeverity.Error,
                Code = "no-x",
                Message = "Не задан натуральный показатель X",
                Field = "quantity",
                SuggestedAction = "Задать X в инспекторе"
            });
        }

        if (a == 0 && b == 0 && pos.Kind == PositionKind.Work)
        {
            issues.Add(new Clarification
            {
                Severity = ClarificationSeverity.Warning,
                Code = "no-ab",
                Message = "Не заданы A и B",
                Field = "priceA",
                SuggestedAction = "Ввести A/B или выбрать интервал"
            });
        }

        var policy = doc.Project.KponPolicy;
        decimal xEff = x;
        decimal? kpon = null;
        var mode = "in-range";

        if (xmin is decimal xMin && xmax is decimal xMax && x >= xMin && x <= xMax && selected?.MaxQuantity is not null)
        {
            xEff = x;
            mode = "in-range";
        }
        else if (xmin is decimal xMin2 && x < xMin2)
        {
            if (x < 0.5m * xMin2)
            {
                issues.Add(new Clarification
                {
                    Severity = ClarificationSeverity.Warning,
                    Code = "x-below-half-min",
                    Message = $"X={x} < 0,5·Xmin ({0.5m * xMin2})",
                    Field = "quantity",
                    SuggestedAction = policy == KponPolicy.AutoKpon
                        ? "Применён Кпон = X/(0,5·Xmin)"
                        : "Задать политику Кпон или экспертный коэффициент"
                });
                if (policy == KponPolicy.AutoKpon)
                {
                    kpon = xMin2 == 0 ? 1m : x / (0.5m * xMin2);
                    xEff = 0.4m * xMin2 + 0.6m * (xMin2 / 2m);
                    mode = "kpon-small";
                }
                else if (policy == KponPolicy.Expert)
                {
                    kpon = 0.8m;
                    xEff = 0.4m * xMin2 + 0.6m * (xMin2 / 2m);
                    mode = "kpon-expert";
                }
                else
                {
                    xEff = 0.4m * xMin2 + 0.6m * x;
                    mode = "below-min";
                }
            }
            else
            {
                xEff = 0.4m * xMin2 + 0.6m * x;
                mode = "below-min";
            }
        }
        else if (xmax is decimal xMax2 && x > xMax2)
        {
            xEff = 0.4m * xMax2 + 0.6m * x;
            mode = "above-max";
            if (x > 2m * xMax2)
            {
                issues.Add(new Clarification
                {
                    Severity = ClarificationSeverity.Warning,
                    Code = "x-above-2max",
                    Message = $"X={x} > 2·Xmax ({2m * xMax2})",
                    Field = "quantity",
                    SuggestedAction = "Проверить экстраполяцию крупных объектов"
                });
            }
        }
        else
        {
            xEff = x;
            mode = "in-range";
        }

        var factors = CollectFactors(doc, estimate, pos);
        var (product, used) = FactorProduct(factors, doc.Project.Stage);
        if (kpon is decimal kp)
            product *= kp;

        var stageFactor = used.FirstOrDefault(f => f.Role == "stage"
            || f.Identifier.Contains("Ки1", StringComparison.OrdinalIgnoreCase)
            || f.Identifier.Contains("Кст", StringComparison.OrdinalIgnoreCase));
        if (stageFactor is null)
        {
            issues.Add(new Clarification
            {
                Severity = ClarificationSeverity.Info,
                Code = "no-ki1",
                Message = "Нет коэффициента стадийности Ки1/Кст. Доли 0,4/0,6 — только дефолт объекта, не схема сборника.",
                Field = "factors",
                SuggestedAction = "Задать Ки1 сборника или оставить дефолт объекта"
            });
            issues.Add(StageSplitIssue(pos));
        }
        else if (IsDefaultStageSplit(stageFactor) && IsCollection17(pos))
        {
            issues.Add(StageSplitIssue(pos));
        }

        var qty = pos.Qty == 0 ? 1m : pos.Qty;
        var baseCost = a + b * xEff;
        var raw = baseCost * product * qty;
        var total = NumberParse.RoundMoney(raw, doc.Project.RoundMode);

        var (symbolic, expanded) = BuildFormulas(a, b, x, xmin, xmax, xEff, mode, used, kpon, doc.Project.Stage, qty);

        return new PositionCalcResult
        {
            Total = total,
            BaseCost = baseCost,
            Xeffective = xEff,
            FactorProduct = product * qty,
            Kpon = kpon,
            Mode = mode,
            ExpandedFormula = expanded,
            SymbolicFormula = symbolic,
            SelectedItem = selected,
            Issues = issues
        };
    }

    private static bool IsDefaultStageSplit(Factor f) =>
        f.ProjectDoc == 0.4m && f.WorkDoc == 0.6m;

    private static bool IsCollection17(Position pos)
    {
        if (pos.Basis?.CollectionNum == 17 && pos.Basis.CanonicalDoc?.Contains("81-2001", StringComparison.Ordinal) == true)
            return true;
        var raw = pos.Basis?.Raw ?? "";
        return raw.Contains("81-2001-17", StringComparison.Ordinal) || raw.Contains("81-02-17-2001", StringComparison.Ordinal);
    }

    private static Clarification StageSplitIssue(Position pos) => new()
    {
        Severity = ClarificationSeverity.Info,
        Code = "stage-split-default",
        Message = IsCollection17(pos)
            ? "Для СБЦП 81-2001-17 в МДС доля П часто 60%, не 40%. Ки1 0,4/0,6 оставлен как дефолт объекта."
            : "Ки1 0,4/0,6 — дефолт объекта, не доля стадий этого сборника.",
        Field = "factors",
        SuggestedAction = "Подставить долю стадий из сборника, если она известна"
    };

    public static IntervalItem? SelectItem(IReadOnlyList<IntervalItem> items, decimal? x)
    {
        if (items.Count == 0 || x is null) return null;
        var xv = x.Value;

        foreach (var it in items)
        {
            if (it.MaxQuantity is decimal max)
            {
                var min = it.MinQuantity ?? decimal.MinValue;
                if (xv >= min && xv <= max)
                    return it;
            }
        }

        IntervalItem? open = null;
        foreach (var it in items)
        {
            if (it.MaxQuantity is null && it.MinQuantity is decimal min && xv > min)
                open = it;
        }
        if (open is not null) return open;

        if (xv < (items[0].MinQuantity ?? 0))
            return items[0];
        return items[^1];
    }

    public static void RebuildSummary(PirDocument doc)
    {
        doc.Summary ??= new Summary1Ps();
        var rows = new List<SummaryRow>();
        decimal design = 0, survey = 0;
        var i = 0;
        foreach (var est in doc.Estimates)
        {
            i++;
            var amount = est.EngineAmount ?? est.ExcelAmount ?? 0m;
            var surveyForm = est.Form is EstimateForm.Form2PSurveyNz or EstimateForm.Form2PSurveySbc;
            var surveyPart = est.Heading || !surveyForm ? 0m : amount;
            var designPart = est.Heading || surveyForm ? 0m : amount;
            var code = est.Number ?? est.Id;
            if (string.IsNullOrWhiteSpace(code))
                code = i.ToString();
            rows.Add(new SummaryRow
            {
                Code = code,
                Title = est.Title,
                Ref = est.Id,
                Heading = est.Heading,
                Survey = surveyPart,
                Design = designPart,
                Total = est.Heading ? 0m : amount
            });
            if (!est.Heading)
            {
                design += designPart;
                survey += surveyPart;
            }
        }

        var baseSum = design + survey;
        var vat = Math.Round(baseSum * doc.Project.VatRate, 2, MidpointRounding.AwayFromZero);
        doc.Summary.Rows = rows;
        doc.Summary.Totals = new SummaryTotals
        {
            Design = Math.Round(design, 2, MidpointRounding.AwayFromZero),
            Survey = Math.Round(survey, 2, MidpointRounding.AwayFromZero),
            Vat = vat,
            Grand = Math.Round(baseSum + vat, 2, MidpointRounding.AwayFromZero)
        };
    }

    private static decimal? EffectiveXmax(IntervalItem? selected, Position pos)
    {
        if (selected?.MaxQuantity is decimal max) return max;
        if (selected?.MaxQuantity is null && selected?.MinQuantity is decimal minOnly)
            return minOnly;
        return pos.Xmax;
    }

    private static List<Factor> CollectFactors(PirDocument doc, Estimate? estimate, Position pos)
    {
        var list = new List<Factor>();
        var posHasStage = pos.Factors.Any(f => f.Enabled && f.Role == "stage");
        if (estimate is not null)
            list.AddRange(estimate.Factors.Where(f => f.Enabled && string.IsNullOrEmpty(f.CatalogKey) && !(posHasStage && f.Role == "stage")));
        var suppressed = new HashSet<string>(pos.SuppressedConditionIds, StringComparer.Ordinal);
        list.AddRange(pos.Factors.Where(f => f.Enabled && (string.IsNullOrEmpty(f.CatalogKey) || !suppressed.Contains(f.CatalogKey))));
        foreach (var condition in ResolveConditions(doc, estimate, pos))
        {
            if (list.Any(f => f.CatalogKey == condition.Id)) continue;
            var use = MatchingUse(condition, estimate, pos);
            if (use is null) continue;
            list.Add(new Factor
            {
                Identifier = condition.Id,
                Caption = condition.Alias,
                ProjectDoc = use.Value,
                WorkDoc = use.Value,
                WorkProject = use.Value,
                CombineType = FactorCombineType.Complicating,
                Role = "complicating",
                Enabled = true,
                Source = FactorSource.Catalog,
                CatalogKey = condition.Id,
                LetterRef = $"{use.BookId} табл. {use.Table} п. {use.Item}"
            });
        }
        return list;
    }

    private static IEnumerable<ProjectCondition> ResolveConditions(PirDocument doc, Estimate? estimate, Position pos)
    {
        var chosen = new Dictionary<string, ProjectCondition>(StringComparer.Ordinal);
        foreach (var condition in doc.Project.Conditions.Where(c => c.Enabled && !string.IsNullOrEmpty(c.Id)))
            chosen[condition.Id] = condition;
        if (estimate is not null)
        {
            foreach (var id in estimate.SuppressedConditionIds)
                chosen.Remove(id);
            foreach (var condition in estimate.Conditions.Where(c => c.Enabled && !string.IsNullOrEmpty(c.Id)))
                chosen[condition.Id] = condition;
        }
        foreach (var id in pos.SuppressedConditionIds)
            chosen.Remove(id);
        return chosen.Values;
    }

    private static FactorUse? MatchingUse(ProjectCondition condition, Estimate? estimate, Position pos)
    {
        var table = pos.Basis?.Table ?? "";
        var raw = $"{pos.Basis?.Raw} {pos.Basis?.Doc} {pos.Basis?.CanonicalDoc}";
        foreach (var use in condition.Uses)
        {
            var linked = estimate?.Volumes.Any(v =>
                v.PositionIds.Contains(pos.Id)
                && string.Equals(v.BookId, use.BookId, StringComparison.OrdinalIgnoreCase)
                && (use.PriceTables.Count == 0 || use.PriceTables.Contains(v.Table ?? ""))) == true;
            if (linked) return use;
            if (table.Length == 0 || !use.PriceTables.Contains(table)) continue;
            if (raw.Contains(use.BookMark, StringComparison.OrdinalIgnoreCase)) return use;
        }
        return null;
    }

    private static (decimal product, List<Factor> used) FactorProduct(IEnumerable<Factor> factors, DesignStage stage)
    {
        var used = factors.Where(f => f.Enabled).ToList();
        decimal product = 1m;
        var pricing = used.Where(f => f.CombineType == FactorCombineType.Pricing).ToList();
        var complicating = used.Where(f => f.CombineType == FactorCombineType.Complicating).ToList();
        var composite = used.Where(f => f.CombineType == FactorCombineType.Composite).ToList();
        var divisors = used.Where(f => f.CombineType == FactorCombineType.Divisor).ToList();

        foreach (var f in pricing)
        {
            var v = StageHelper.Pick(f, stage);
            if (v is decimal d) product *= d;
        }

        if (complicating.Count > 0)
        {
            decimal acc = 1m;
            foreach (var f in complicating)
            {
                var v = StageHelper.Pick(f, stage);
                if (v is decimal d) acc += d - 1m;
            }
            product *= acc;
        }

        if (composite.Count > 0)
        {
            decimal acc = 0m;
            foreach (var f in composite)
            {
                var v = StageHelper.Pick(f, stage);
                if (v is decimal d) acc += d;
            }
            product *= acc;
        }

        foreach (var f in divisors)
        {
            var v = StageHelper.Pick(f, stage);
            if (v is decimal d and not 0)
                product /= d;
        }

        return (product, used);
    }

    private static (string symbolic, string expanded) BuildFormulas(
        decimal a, decimal b, decimal x, decimal? xmin, decimal? xmax, decimal xEff,
        string mode, List<Factor> used, decimal? kpon, DesignStage stage, decimal qty)
    {
        string ids = "";
        string nums = "";
        foreach (var f in used)
        {
            var v = StageHelper.Pick(f, stage);
            if (v is null) continue;
            var id = string.IsNullOrWhiteSpace(f.Identifier) ? "K" : f.Identifier;
            ids += "*" + id;
            nums += "*" + NumberParse.FormatCoeff(v.Value);
        }
        if (kpon is decimal kp)
        {
            ids += "*Кпон";
            nums += "*" + NumberParse.FormatCoeff(kp);
        }
        if (qty != 1m)
        {
            ids += "*Кол";
            nums += "*" + NumberParse.FormatCoeff(qty);
        }

        string symbolic;
        string expanded;
        var aTh = NumberParse.FormatThousands(a);
        var bTh = NumberParse.FormatThousands(b);

        switch (mode)
        {
            case "below-min":
                symbolic = $"(A+B*(0.4*Xmin+0.6*X)){ids}";
                expanded = $"({aTh}+{bTh}*(0.4*{xmin}+0.6*{x})){nums}";
                break;
            case "above-max":
                symbolic = $"(A+B*(0.4*X2+0.6*X)){ids}";
                expanded = $"({aTh}+{bTh}*(0.4*{xmax}+0.6*{x})){nums}";
                break;
            case "kpon-small":
            case "kpon-expert":
                symbolic = $"(A+B*(0.4*Xmin+0.6*(Xmin/2))){ids}";
                expanded = $"({aTh}+{bTh}*(0.4*{xmin}+0.6*({xmin}/2))){nums}";
                break;
            default:
                symbolic = $"(A+B*X){ids}";
                expanded = $"({aTh}+{bTh}*{x}){nums}";
                break;
        }

        return (symbolic, expanded);
    }

    private static void UpdateRelativePrices(Position pos, decimal total, DesignStage stage)
    {
        foreach (var r in pos.RelativeFactors.Where(r => r.Enabled))
        {
            var pct = stage switch
            {
                DesignStage.WorkDoc => r.WorkDoc ?? r.ProjectDoc,
                DesignStage.WorkProject => r.WorkProject ?? r.WorkDoc ?? r.ProjectDoc,
                _ => r.ProjectDoc ?? r.WorkDoc
            };
            if (pct is decimal p)
                r.RelativePrice = Math.Round(total * p / 100m, 2, MidpointRounding.AwayFromZero);
        }
    }

    internal static bool IsLaborForm(EstimateForm form) =>
        form is EstimateForm.Form3P or EstimateForm.LaborCost;

    private static PositionCalcResult CalculateLaborLine(Position pos)
    {
        var heads = pos.Qty == 0 ? 1m : pos.Qty;
        var days = pos.Quantity ?? 0m;
        var rate = pos.PriceA ?? 0m;
        var pay = ExcelRoundFormula.Round2(heads * days * rate);
        return new PositionCalcResult
        {
            Total = pay,
            BaseCost = pay,
            Xeffective = days,
            FactorProduct = heads,
            Mode = "labor-3p",
            SymbolicFormula = "Чел.×чел-дн×ставка",
            ExpandedFormula = $"{heads}×{days}×{rate}"
        };
    }

    private static void ApplyLaborTotals(Estimate est)
    {
        var labor = est.Labor ??= new LaborModel();
        var share = labor.SalaryShare == 0 ? 0.45m : labor.SalaryShare;
        var salary = ExcelRoundFormula.Round2(
            est.Positions.Where(p => p.Kind == PositionKind.Work)
                .Sum(p => (p.Qty == 0 ? 1m : p.Qty) * (p.Quantity ?? 0m) * (p.PriceA ?? 0m)));
        var prime = ExcelRoundFormula.Round2(salary / share);
        var profit = ExcelRoundFormula.Round2(prime * labor.ProfitPercent / 100m);
        var cost = ExcelRoundFormula.Round2((prime + profit) * labor.RegionalK);
        labor.SalaryTotal = salary;
        labor.PrimeCost = prime;
        labor.Profit = profit;
        est.EngineAmount = cost;
    }

    private static PositionCalcResult CalculateAsutpPart(Position pos, PirDocument doc, Estimate est)
    {
        var scores = pos.Quantity ?? 0m;
        var mult = pos.PriceA ?? 0m;
        var kp = pos.Qty == 0 ? 1m : pos.Qty;
        var (product, used) = FactorProduct(CollectFactors(doc, est, pos), doc.Project.Stage);
        var tys = scores * mult * kp * product;
        var rub = tys * 1000m;
        var ids = string.Join("*", used.Select(f => f.Identifier).Where(id => !string.IsNullOrWhiteSpace(id)));
        return new PositionCalcResult
        {
            Total = rub,
            BaseCost = scores * mult * kp,
            Xeffective = scores,
            FactorProduct = product,
            Mode = "asutp-sbc",
            SymbolicFormula = string.IsNullOrEmpty(ids) ? "Бч×S×КП×1000" : $"Бч×S×КП×{ids}×1000",
            ExpandedFormula = $"{scores}×{mult}×{kp}×{NumberParse.FormatCoeff(product)}×1000"
        };
    }
}
