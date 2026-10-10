using System.Globalization;
using System.Text.RegularExpressions;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Calc;

/// <summary>
/// Разбор Excel-формулы стоимости 2П: ROUND/ОКРУГЛ и цепочки (A+B*X)*Ki.
/// Десятичная запятая (1,56) и точка (1.56) равноправны; разделитель аргументов — ; или ,.
/// </summary>
public sealed class ParsedExcelFormula
{
    public decimal? PriceA { get; init; }
    public decimal? PriceB { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? Xmin { get; init; }
    public decimal? Qty { get; init; }
    public bool BelowMinExtrapolation { get; init; }
    public bool AboveMaxExtrapolation { get; init; }
    public List<decimal> Multipliers { get; } = [];
    public decimal? Evaluated { get; init; }
    public string Normalized { get; init; } = "";
    public string? Inner { get; init; }
    public bool IsRound { get; init; }
    public int RoundDigits { get; init; } = 2;
}

public static partial class ExcelRoundFormula
{
    public static ParsedExcelFormula? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var original = raw.Trim();
        var (expr, isRound, digits, russian) = UnwrapRound(original);
        if (string.IsNullOrWhiteSpace(expr)) return null;

        expr = expr.Replace("\u00A0", "").Replace(" ", "");
        expr = expr.Replace("×", "*").Replace("х", "*", StringComparison.OrdinalIgnoreCase);
        // «15x15» в СБЦ, но не трогаем уже разобранный ROUND.
        expr = XAsMultiply().Replace(expr, "*");
        if (russian)
            expr = ReplaceDecimalCommas(expr);

        var below = false;
        var above = false;
        var hasAbx = false;
        decimal? a = null, b = null, x = null, xmin = null, qty = null;
        var multipliers = new List<decimal>();

        var extra = Regex.Match(expr,
            @"^\(?(?<a>\d+(?:\.\d+)?)\+(?<b>\d+(?:\.\d+)?)\*\(0\.4\*(?<xmin>\d+(?:\.\d+)?)\+0\.6\*(?<x>\d+(?:\.\d+)?)\)\)?(?<rest>.*)$",
            RegexOptions.IgnoreCase);
        if (extra.Success)
        {
            a = NumberParse.TryParse(extra.Groups["a"].Value);
            b = NumberParse.TryParse(extra.Groups["b"].Value);
            xmin = NumberParse.TryParse(extra.Groups["xmin"].Value);
            x = NumberParse.TryParse(extra.Groups["x"].Value);
            below = true;
            hasAbx = true;
            multipliers.AddRange(SplitMultipliers(extra.Groups["rest"].Value));
        }
        else
        {
            var extraMax = Regex.Match(expr,
                @"^\(?(?<a>\d+(?:\.\d+)?)\+(?<b>\d+(?:\.\d+)?)\*\(0\.4\*(?<xmax>\d+(?:\.\d+)?)\+0\.6\*(?<x>\d+(?:\.\d+)?)\)\)?(?<rest>.*)$",
                RegexOptions.IgnoreCase);
            if (extraMax.Success)
            {
                a = NumberParse.TryParse(extraMax.Groups["a"].Value);
                b = NumberParse.TryParse(extraMax.Groups["b"].Value);
                x = NumberParse.TryParse(extraMax.Groups["x"].Value);
                above = true;
                hasAbx = true;
                multipliers.AddRange(SplitMultipliers(extraMax.Groups["rest"].Value));
            }
            else
            {
                var abx = Regex.Match(expr,
                    @"^\(?(?<a>\d+(?:\.\d+)?)\+(?<b>\d+(?:\.\d+)?)\*(?<x>\d+(?:\.\d+)?)\)?(?<rest>.*)$",
                    RegexOptions.IgnoreCase);
                if (abx.Success)
                {
                    a = NumberParse.TryParse(abx.Groups["a"].Value);
                    b = NumberParse.TryParse(abx.Groups["b"].Value);
                    x = NumberParse.TryParse(abx.Groups["x"].Value);
                    hasAbx = true;
                    multipliers.AddRange(SplitMultipliers(abx.Groups["rest"].Value));
                }
                else
                {
                    multipliers.AddRange(SplitMultipliers(expr.StartsWith('*') ? expr : "*" + expr));
                    if (multipliers.Count > 0)
                    {
                        a = multipliers[0];
                        b = 0;
                        x = 0;
                        multipliers.RemoveAt(0);
                    }
                }
            }
        }

        if (hasAbx && multipliers.Count > 0 && multipliers[0] is > 0 and <= 100)
            qty = multipliers[0];

        decimal? evaluated = null;
        try
        {
            evaluated = Evaluate(a, b, x, xmin, below, multipliers, isRound, digits);
        }
        catch
        {
            /* ignore */
        }

        return new ParsedExcelFormula
        {
            PriceA = a,
            PriceB = b,
            Quantity = x,
            Xmin = xmin,
            Qty = qty,
            BelowMinExtrapolation = below,
            AboveMaxExtrapolation = above,
            Evaluated = evaluated,
            Normalized = expr,
            Inner = expr,
            IsRound = isRound,
            RoundDigits = digits
        }.WithMultipliers(multipliers);
    }

    public static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static ParsedExcelFormula WithMultipliers(this ParsedExcelFormula p, List<decimal> m)
    {
        p.Multipliers.AddRange(m);
        return p;
    }

    private static (string expr, bool isRound, int digits, bool russian) UnwrapRound(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith('=')) s = s[1..];
        var russian = s.Contains("ОКРУГЛ", StringComparison.OrdinalIgnoreCase) || s.Contains(';');
        var m = Regex.Match(s, @"^(?:ROUND|_xlfn\.ROUND|ОКРУГЛ)\((.*)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success)
            return (s, false, 2, russian);

        var inner = m.Groups[1].Value.Trim();
        var digits = 2;
        if (russian && inner.Contains(';'))
        {
            var idx = inner.LastIndexOf(';');
            digits = (int)(NumberParse.TryParse(inner[(idx + 1)..]) ?? 2);
            inner = inner[..idx];
        }
        else
        {
            var split = SplitTopComma(inner);
            if (split.Count >= 2 && NumberParse.TryParse(split[^1]) is decimal d && d is >= 0 and <= 10)
            {
                digits = (int)d;
                inner = string.Join(",", split.Take(split.Count - 1));
            }
        }

        return (inner, true, digits, russian);
    }

    private static List<string> SplitTopComma(string s)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    /// <summary>Запятая между цифрами — десятичная (1,56), не разделитель тысяч (31 100 уже без пробелов).</summary>
    private static string ReplaceDecimalCommas(string expr)
    {
        return Regex.Replace(expr, @"(\d),(\d)", "$1.$2");
    }

    private static List<decimal> SplitMultipliers(string rest)
    {
        var list = new List<decimal>();
        if (string.IsNullOrWhiteSpace(rest)) return list;
        var t = rest.Trim().TrimStart('*');
        if (t.Length == 0) return list;
        foreach (var part in t.Split('*', StringSplitOptions.RemoveEmptyEntries))
        {
            var v = NumberParse.TryParse(part.Trim().TrimStart('*').TrimEnd(')'));
            if (v is decimal d) list.Add(d);
        }
        return list;
    }

    private static decimal? Evaluate(decimal? a, decimal? b, decimal? x, decimal? xmin, bool below,
        List<decimal> multipliers, bool isRound, int digits)
    {
        if (a is null) return null;
        decimal xEff = x ?? 0;
        if (below && xmin is decimal xn && x is decimal xv)
            xEff = 0.4m * xn + 0.6m * xv;
        var raw = a.Value + (b ?? 0) * xEff;
        foreach (var m in multipliers)
            raw *= m;
        if (isRound)
            return Math.Round(raw, digits, MidpointRounding.AwayFromZero);
        return raw;
    }

    [GeneratedRegex(@"(?<=\d)[xх](?=\d)", RegexOptions.IgnoreCase)]
    private static partial Regex XAsMultiply();
}

public static class FactorBinder
{
    public static Factor Make(string id, decimal value, string role, FactorSource source, FactorCombineType combine = FactorCombineType.Pricing) => new()
    {
        Identifier = id,
        Caption = id,
        Role = role,
        ProjectDoc = value,
        WorkDoc = value,
        WorkProject = value,
        CombineType = combine,
        Source = source,
        Enabled = true
    };

    public static void Upsert(Position pos, string id, decimal value, string role, FactorSource source)
    {
        var f = pos.Factors.FirstOrDefault(x => string.Equals(x.Identifier, id, StringComparison.OrdinalIgnoreCase));
        if (f is null)
            pos.Factors.Add(Make(id, value, role, source));
        else
        {
            f.ProjectDoc = value;
            f.WorkDoc = value;
            f.WorkProject = value;
            f.Role = role;
            if (f.Source != source && f.Source != FactorSource.Unknown)
                f.Source = FactorSource.Both;
            else
                f.Source = source;
        }
    }

    public static string GuessRole(string id, decimal value, string? context)
    {
        var t = (id + " " + context).ToLowerInvariant();
        if (t.Contains("ктек") || t.Contains("инд.") || t.Contains("инфляц") || t.Contains("текущ"))
            return "index";
        if (t.Contains("кст") || t.Contains("ки1") || t.Contains("стади"))
            return "stage";
        if (t.Contains("кпон") || t.Contains("пониж"))
            return "kpon";
        if (t.Contains("газпром") || t.Contains("оптимизац"))
            return "departmental";
        if (t.Contains("районн"))
            return "district";
        if (value is 0.5m or 0.52m or 0.58m or 0.6m or 0.4m or 1m)
            return t.Contains("инфляц") ? "index" : "stage";
        return "other";
    }

    public static FactorCombineType FromAdeptType(string? typeName, int? typeNum)
    {
        if (typeNum == 1 || string.Equals(typeName, "Ценообразующий", StringComparison.OrdinalIgnoreCase))
            return FactorCombineType.Pricing;
        if (typeNum == 2 || string.Equals(typeName, "Составной", StringComparison.OrdinalIgnoreCase))
            return FactorCombineType.Composite;
        if (typeNum == 3 || string.Equals(typeName, "Усложняющий", StringComparison.OrdinalIgnoreCase))
            return FactorCombineType.Complicating;
        if (typeNum == 4 || string.Equals(typeName, "Делитель", StringComparison.OrdinalIgnoreCase))
            return FactorCombineType.Divisor;
        return FactorCombineType.Pricing;
    }
}
