using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Excel;

public static class DialectDetector
{
    public static EstimateForm Detect(SheetTable sheet)
    {
        var name = sheet.Name ?? "";
        var head = sheet.Concat(25, 12);

        if (name.Contains("СВОД", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Форма 1пс", StringComparison.OrdinalIgnoreCase)
            || head.Contains("СВОДНАЯ СМЕТА", StringComparison.OrdinalIgnoreCase))
            return EstimateForm.Form1Ps;

        if (head.Contains("человеко-дн", StringComparison.OrdinalIgnoreCase)
            || head.Contains("форма 3П", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Форма 3П", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Определение трудоемкости", StringComparison.OrdinalIgnoreCase)
            || head.Contains("трудозатрат", StringComparison.OrdinalIgnoreCase)
            || (head.Contains("себестоимости", StringComparison.OrdinalIgnoreCase)
                && head.Contains("рентабельн", StringComparison.OrdinalIgnoreCase)))
            return EstimateForm.Form3P;

        if (head.Contains("707/пр", StringComparison.OrdinalIgnoreCase)
            || head.Contains("форма 2П, 707", StringComparison.OrdinalIgnoreCase))
            return EstimateForm.Form2P707;

        if (name.Contains("АСУТП", StringComparison.OrdinalIgnoreCase)
            || name.Contains("АСУЭ", StringComparison.OrdinalIgnoreCase)
            || name.Contains("АСУ Э", StringComparison.OrdinalIgnoreCase)
            || name.Contains("АЭО", StringComparison.OrdinalIgnoreCase)
            || name.Contains("СПБА", StringComparison.OrdinalIgnoreCase)
            || head.Contains("АСУТП", StringComparison.OrdinalIgnoreCase)
            || head.Contains("АСУ ТП", StringComparison.OrdinalIgnoreCase)
            || head.Contains("Автоматизированные системы управления технологическими", StringComparison.OrdinalIgnoreCase))
            return EstimateForm.AsutpCard;

        if (head.Contains("Форма 2П", StringComparison.OrdinalIgnoreCase)
            || head.Contains("форма 2П", StringComparison.OrdinalIgnoreCase))
            return EstimateForm.Form2PSbc;

        return EstimateForm.Unknown;
    }
}
