using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Excel;

/// <summary>
/// Форма 3П МДС 81-35.2004 / печать Adept <c>3POldstylesheet.xsl</c>:
/// раздел 1 — исполнители × чел-дни × ставка; раздел 2 — з/п / 0.45 + рентабельность.
/// </summary>
internal static class Form3PParser
{
    public static Estimate Parse(SheetTable s, PirDocument doc)
    {
        var labor = new LaborModel();
        var est = new Estimate
        {
            Form = EstimateForm.Form3P,
            Sheet = s.Name,
            Factors = [],
            Labor = labor,
            Title = ExcelImporter.FirstNonEmpty(s, 3, 4, 0, 1, 10, 1) ?? s.Name,
            Number = ExcelImporter.ExtractSmetaNumber(s) ?? ExcelImporter.GuessNumberFromSheet(s.Name)
        };
        est.Id = est.Number ?? s.Name;

        string? workName = null;
        for (var r = 0; r < s.Rows; r++)
        {
            var label = (s.Display(r, 1) + " " + s.Display(r, 0)).Trim();
            var role = s.Display(r, 3).Trim();
            var heads = s.Number(r, 2);
            var days = s.Number(r, 4);
            var rate = s.Number(r, 5);

            if (label.Contains("Коэффициент отношения", StringComparison.OrdinalIgnoreCase))
            {
                var v = ExcelImporter.FirstNumber(s, r, 6, 5, 4);
                if (v is > 0 and < 1) labor.SalaryShare = v.Value;
            }
            if (label.Contains("рентабельн", StringComparison.OrdinalIgnoreCase))
            {
                var v = ExcelImporter.FirstNumber(s, r, 6, 5, 4);
                if (v is decimal p)
                    labor.ProfitPercent = p > 1 ? p : p * 100m;
            }
            if (label.Contains("Применяемый коэффициент", StringComparison.OrdinalIgnoreCase)
                || label.Contains("районн", StringComparison.OrdinalIgnoreCase)
                   && label.Contains("коэффициент", StringComparison.OrdinalIgnoreCase))
            {
                var v = ExcelImporter.FirstNumber(s, r, 6, 5, 4);
                if (v is decimal rk && rk > 0) labor.RegionalK = rk;
            }
            if (label.Contains("Всего по смете", StringComparison.OrdinalIgnoreCase)
                || label.Contains("Всего стоимость", StringComparison.OrdinalIgnoreCase))
            {
                est.ExcelAmount = ExcelImporter.FirstNumber(s, r, 6, 5, 4);
                est.ExcelAmount ??= NumberParse.TryParse(s.Display(r, 6));
            }

            if (string.IsNullOrWhiteSpace(role) || role.Contains("должность", StringComparison.OrdinalIgnoreCase))
                continue;
            if (heads is null || days is null || rate is null || rate < 100)
                continue;
            if (label.Contains("Итого", StringComparison.OrdinalIgnoreCase))
                continue;

            var title = s.Display(r, 1).Trim();
            if (title.Length > 8) workName = title;

            var pay = ExcelRoundFormula.Round2(heads.Value * days.Value * rate.Value);
            est.Positions.Add(new Position
            {
                Number = (est.Positions.Count + 1).ToString(),
                Name = role,
                Kind = PositionKind.Work,
                Qty = heads.Value,
                Quantity = days.Value,
                QuantityUnit = "чел-дн",
                PriceA = rate.Value,
                PriceB = 0,
                ExcelAmount = pay,
                Formula = "Чел.×чел-дн×ставка",
                Origin = OriginKind.Excel,
                SheetName = s.Name,
                EstimateNumber = est.Number,
                Section = workName,
                Basis = new Basis { Kind = "LABOR", Raw = role }
            });
        }

        if (string.IsNullOrWhiteSpace(est.Title) || est.Title == s.Name)
            est.Title = workName ?? est.Title;

        if (est.Positions.Count == 0)
        {
            doc.Issues.Add(new Clarification
            {
                Severity = ClarificationSeverity.Warning,
                Code = "labor-partial",
                Message = $"Лист «{s.Name}»: форма 3П без строк исполнителей.",
                EstimateId = est.Id
            });
        }

        return est;
    }
}
