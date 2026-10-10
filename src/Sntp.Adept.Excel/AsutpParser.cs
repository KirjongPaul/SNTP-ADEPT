using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Excel;

/// <summary>
/// СБЦ АСУТП 2016: баллы по частям ТЗ–ПО, цепочка К (ПН×ПВ), Крк, Кин, Кст(Р), ×1000, понижающий К к итогу.
/// Итог — формула ROUND в OpenXML (D174 / Y174), не отображаемый текст.
/// </summary>
internal static class AsutpParser
{
    private static readonly string[] Parts = ["ТЗ", "ОР", "ОО", "ИО", "ТО", "МО", "ПО"];

    public static Estimate Parse(SheetTable s, PirDocument doc)
    {
        var asutp = new AsutpModel { Catalog = "СБЦ АСУТП 2016" };
        var est = new Estimate
        {
            Form = EstimateForm.AsutpCard,
            Sheet = s.Name,
            Factors = [],
            Asutp = asutp,
            Title = ExcelImporter.FirstNonEmpty(s, 4, 1, 3, 1) ?? s.Name,
            Number = ExcelImporter.ExtractSmetaNumber(s) ?? ExcelImporter.GuessNumberFromSheet(s.Name)
        };
        est.Id = est.Number ?? s.Name;

        for (var r = 0; r < Math.Min(s.Rows, 25); r++)
        {
            var row = s.Display(r, 1) + " " + s.Display(r, 6);
            if (row.Contains("Стадия", StringComparison.OrdinalIgnoreCase))
            {
                var st = ExcelImporter.FirstNonEmpty(s, r, 6, r, 7);
                if (!string.IsNullOrWhiteSpace(st) && !st.Contains("Стадия", StringComparison.Ordinal))
                    est.PriceLevel = st;
            }
        }

        asutp.NeedTz = FindNeedTz(s);
        var partCols = FindPartColumns(s);
        var enabled = ReadEnabledParts(s, partCols);
        asutp.EnabledParts = enabled.Where(kv => kv.Value).Select(kv => kv.Key).ToList();

        var scores = ReadKeyedRow(s, partCols, "Расчетное количество баллов");
        var multipliers = ReadKeyedRow(s, partCols, "Ценностные множители");
        var kp = ReadKeyedRow(s, partCols, "Кол-во КП");
        var kst = ReadKeyedRow(s, partCols, "Кст (Р)", "Кст(Р)");
        var krk = FirstNamedValue(s, "К рк", "Районный коэффициент") ?? 1m;
        var kin = FirstNamedValue(s, "К ин", "Коэффициент инфляции") ?? 1m;

        var coeffRows = ReadCoeffRows(s, partCols);

        decimal? total = null;
        string? totalFormula = null;
        for (var r = s.Rows - 1; r >= 0; r--)
        {
            var t = s.Display(r, 1) + " " + s.Display(r, 9);
            if (t.Contains("Итого цена стадии", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Итого цена стадии", StringComparison.OrdinalIgnoreCase))
            {
                total = ExcelImporter.FirstNumber(s, r, 3, 4, 24);
                totalFormula = ExcelImporter.FirstFormula(s, r, 3, 4, 24);
                if (total is not null) break;
            }
        }

        asutp.CorrectionK = ReadTrailingMultiplier(s, "Понижающий коэфф", 0.7m);
        asutp.GazpromK = ReadTrailingMultiplier(s, "за вычетом", 0.8m);
        est.ExcelAmount = total;

        var n = 0;
        foreach (var part in Parts)
        {
            if (!partCols.TryGetValue(part, out var col)) continue;
            var score = scores.GetValueOrDefault(part);
            if (score is null or 0) continue;
            if (enabled.Count > 0 && enabled.TryGetValue(part, out var on) && !on)
                continue;
            n++;
            var pos = new Position
            {
                Number = n.ToString(),
                Name = part,
                Kind = PositionKind.Work,
                Quantity = score,
                QuantityUnit = "балл",
                PriceA = multipliers.GetValueOrDefault(part) ?? 0,
                PriceB = 0,
                Qty = kp.GetValueOrDefault(part) is decimal q && q > 0 ? q : 1m,
                Origin = OriginKind.Excel,
                SheetName = s.Name,
                EstimateNumber = est.Number,
                Section = part,
                ExcelFormula = totalFormula,
                Basis = BasisParser.Parse($"СБЦ АСУТП 2016 · {part}"),
                CatalogCode = "SBC-81-2001-22",
                Formula = "Бч×S×КП×∏Ki×1000"
            };

            foreach (var coeff in coeffRows)
            {
                if (!coeff.Enabled) continue;
                if (!coeff.ByPart.TryGetValue(part, out var v) || v is null) continue;
                if (v == 1m && !coeff.Force) continue;
                FactorBinder.Upsert(pos, coeff.Id, v.Value, coeff.Role, FactorSource.Formula);
            }

            if (kst.GetValueOrDefault(part) is decimal ks && ks != 1m)
                FactorBinder.Upsert(pos, "Кст", ks, "stage", FactorSource.Formula);
            if (krk != 1m)
                FactorBinder.Upsert(pos, "Крк", krk, "district", FactorSource.Formula);
            if (kin != 1m)
                FactorBinder.Upsert(pos, "Кин", kin, "index", FactorSource.Formula);

            est.Positions.Add(pos);
        }

        if (est.Positions.Count == 0)
        {
            doc.Issues.Add(new Clarification
            {
                Severity = ClarificationSeverity.Warning,
                Code = "asutp-partial",
                Message = $"Лист «{s.Name}»: карточка АСУТП без частей с баллами.",
                EstimateId = est.Id,
                SuggestedAction = "Проверить итог и баллы ТЗ–ПО"
            });
        }

        return est;
    }

    private sealed record CoeffRow(string Id, string Role, bool Enabled, bool Force, Dictionary<string, decimal?> ByPart);

    private static Dictionary<string, int> FindPartColumns(SheetTable s)
    {
        Dictionary<string, int>? best = null;
        var bestHits = 0;
        for (var r = 0; r < s.Rows; r++)
        {
            var local = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < Math.Min(s.Cols, 16); c++)
            {
                var t = s.Display(r, c).Trim();
                if (Parts.Contains(t, StringComparer.OrdinalIgnoreCase) && !local.ContainsKey(t))
                    local[t] = c;
            }
            if (local.Count > bestHits)
            {
                best = local;
                bestHits = local.Count;
            }
        }
        if (bestHits >= 4 && best is not null)
            return best;
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Parts.Length; i++)
            map[Parts[i]] = 2 + i;
        return map;
    }

    private static Dictionary<string, bool> ReadEnabledParts(SheetTable s, Dictionary<string, int> partCols)
    {
        var result = Parts.ToDictionary(p => p, _ => false, StringComparer.OrdinalIgnoreCase);
        for (var r = 0; r < Math.Min(s.Rows, 30); r++)
        {
            if (!s.Display(r, 1).Contains("Требуемые части", StringComparison.OrdinalIgnoreCase)
                && r + 1 < s.Rows
                && !Parts.Contains(s.Display(r, 3).Trim(), StringComparer.OrdinalIgnoreCase))
                continue;
            var header = r;
            if (!Parts.Contains(s.Display(r, 3).Trim(), StringComparer.OrdinalIgnoreCase))
                header = r + 1;
            var data = header + 1;
            if (data >= s.Rows) break;
            foreach (var (part, col) in partCols)
            {
                var v = s.Display(data, col).Trim();
                if (v.Equals("Да", StringComparison.OrdinalIgnoreCase))
                    result[part] = true;
            }
            break;
        }
        return result;
    }

    private static bool FindNeedTz(SheetTable s)
    {
        for (var r = 0; r < Math.Min(s.Rows, 25); r++)
        {
            if (s.Display(r, 1).Contains("разработку ТЗ", StringComparison.OrdinalIgnoreCase))
                return s.Display(r, 7).Equals("Да", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static Dictionary<string, decimal?> ReadKeyedRow(SheetTable s, Dictionary<string, int> partCols, params string[] keys)
    {
        var map = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        for (var r = 0; r < s.Rows; r++)
        {
            var t = s.Display(r, 9) + " " + s.Display(r, 1) + " " + s.Display(r, 10);
            if (!keys.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;
            foreach (var (part, col) in partCols)
                map[part] = s.Number(r, col);
            return map;
        }
        return map;
    }

    private static decimal? FirstNamedValue(SheetTable s, params string[] keys)
    {
        for (var r = 0; r < s.Rows; r++)
        {
            var t = s.Display(r, 9) + " " + s.Display(r, 10);
            if (!keys.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;
            return ExcelImporter.FirstNumber(s, r, 2, 3, 16, 6);
        }
        return null;
    }

    private static List<CoeffRow> ReadCoeffRows(SheetTable s, Dictionary<string, int> partCols)
    {
        var list = new List<CoeffRow>();
        for (var r = 0; r < s.Rows; r++)
        {
            var id = s.Display(r, 9).Trim();
            var note = s.Display(r, 10).Trim();
            if (id.Length == 0 || id.Equals("Обозначение", StringComparison.OrdinalIgnoreCase))
                continue;
            if (note.Contains("Итого", StringComparison.OrdinalIgnoreCase)
                || note.Contains("ИТОГО", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!(id.StartsWith('К') || id.StartsWith('K') || id.StartsWith("К ", StringComparison.Ordinal)))
                continue;
            if (id.Contains("Кст", StringComparison.OrdinalIgnoreCase)
                || id.Equals("Кт", StringComparison.OrdinalIgnoreCase)
                || id.Contains("К рк", StringComparison.OrdinalIgnoreCase)
                || id.Contains("К ин", StringComparison.OrdinalIgnoreCase))
                continue;

            var type = s.Display(r, 10).Trim();
            var exec = s.Display(r, 17).Trim();
            var enabled = exec.Equals("Да", StringComparison.OrdinalIgnoreCase) || exec.Length == 0;
            var byPart = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (part, col) in partCols)
                byPart[part] = s.Number(r, col);
            var role = type.Contains("ПН", StringComparison.OrdinalIgnoreCase) ? "kpon"
                : FactorBinder.GuessRole(id, byPart.Values.FirstOrDefault() ?? 1m, s.Display(r, 11));
            list.Add(new CoeffRow(NormalizeId(id), role, enabled, Force: enabled, byPart));
        }
        return list;
    }

    private static string NormalizeId(string id) =>
        id.Replace(" ", "", StringComparison.Ordinal).Replace("об", "об", StringComparison.OrdinalIgnoreCase);

    private static decimal ReadTrailingMultiplier(SheetTable s, string hint, decimal fallback)
    {
        for (var r = s.Rows - 1; r >= 0; r--)
        {
            var t = s.Display(r, 9) + s.Display(r, 1);
            if (!t.Contains(hint, StringComparison.OrdinalIgnoreCase)) continue;
            var f = ExcelImporter.FirstFormula(s, r, 3, 4, 24);
            if (f is not null)
            {
                var parsed = ExcelRoundFormula.Parse(f);
                if (parsed?.Multipliers.Count > 0)
                    return parsed.Multipliers[^1];
                var star = f.LastIndexOf('*');
                if (star >= 0)
                {
                    var tail = f[(star + 1)..].Trim().TrimEnd(')', ',');
                    if (NumberParse.TryParse(tail) is decimal m && m is > 0 and < 2)
                        return m;
                }
            }
            if (ExcelImporter.FirstNumber(s, r, 16, 20) is decimal v && v is > 0 and < 2)
                return v;
        }
        return fallback;
    }
}
