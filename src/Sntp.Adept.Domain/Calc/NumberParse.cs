using System.Globalization;
using System.Text.RegularExpressions;

namespace Sntp.Adept.Domain.Calc;

public static partial class NumberParse
{
    public static decimal? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().Replace("\u00A0", "").Replace(" ", "");
        if (s.Length == 0) return null;

        if (s.Contains(',') && s.Contains('.'))
        {
            if (s.LastIndexOf(',') > s.LastIndexOf('.'))
                s = s.Replace(".", "").Replace(',', '.');
            else
                s = s.Replace(",", "");
        }
        else if (s.Contains(','))
        {
            s = s.Replace(',', '.');
        }

        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return null;
    }

    public static decimal ParseOrZero(string? raw) => TryParse(raw) ?? 0m;

    public static decimal RoundMoney(decimal value, Models.RoundMode mode) => mode switch
    {
        Models.RoundMode.Round10 => Math.Round(value / 10m, 0, MidpointRounding.AwayFromZero) * 10m,
        Models.RoundMode.Round1 => Math.Round(value, 0, MidpointRounding.AwayFromZero),
        _ => value
    };

    /// <summary>Формат как в ExpandedFormula образца: тысячи с запятой (8,64 / 0,017).</summary>
    public static string FormatThousands(decimal rubles)
    {
        var t = rubles / 1000m;
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        if (t == decimal.Truncate(t))
            return t.ToString("0", ru);
        var s = t.ToString("0.#####", ru);
        return s;
    }

    public static string FormatCoeff(decimal v)
    {
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        return v.ToString("0.#####", ru);
    }

    [GeneratedRegex(@"[-+]?\d[\d\s]*(?:[.,]\d+)?")]
    private static partial Regex NumToken();

    public static decimal? FirstNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = NumToken().Match(text);
        return m.Success ? TryParse(m.Value) : null;
    }
}
