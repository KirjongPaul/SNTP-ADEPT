using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Sntp.Adept.Domain.Catalog;

namespace Sntp.Adept.Harvest;

/// <summary>
/// Читает видимую сетку «Базы ПИР» уже запущенного Адепт.
/// Кнопку «Добавить» не нажимает и верхнее дерево смет не трогает.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var expect = Arg(args, "--version");
        var probe = args.Contains("--probe");
        var checkpointPath = Path.Combine(AppContext.BaseDirectory, "harvest-checkpoint.json");
        using var automation = new UIA3Automation();
        var window = automation.GetDesktop().FindAllChildren(c => c.ByControlType(ControlType.Window))
            .FirstOrDefault(w => w.Name.StartsWith("Адепт", StringComparison.Ordinal));
        if (window is null)
        {
            Console.Error.WriteLine("Окно «Адепт:» не найдено. Откройте Адепт и повторите.");
            return 2;
        }

        var title = window.Name;
        var seen = ReadCheckpoint(checkpointPath);
        if (!string.IsNullOrWhiteSpace(expect) && !title.Contains(expect, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Заголовок «{title}» не содержит версию {expect}. Съём остановлен.");
            return 3;
        }
        if (seen?.Title is not null && !string.Equals(seen.Title, title, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Версия окна сменилась: было «{seen.Title}», сейчас «{title}». Съём остановлен.");
            return 3;
        }

        if (probe)
        {
            Dump(window, 0, 4);
            return 0;
        }

        var rows = ReadGrid(window);
        if (rows.Count == 0)
        {
            Console.Error.WriteLine("Сетка не отдала строки UIA. Запустите с --probe и посмотрите дерево. Распознавание картинки не используется.");
            Dump(window, 0, 3);
            return 4;
        }

        var last = rows[^1].Code;
        File.WriteAllText(checkpointPath, JsonSerializer.Serialize(new Checkpoint(title, last)));
        Console.WriteLine(JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static List<AdeptUiRate.HarvestRow> ReadGrid(AutomationElement window)
    {
        var list = new List<AdeptUiRate.HarvestRow>();
        var grids = window.FindAllDescendants(c => c.ByControlType(ControlType.DataGrid))
            .Concat(window.FindAllDescendants(c => c.ByControlType(ControlType.Table)));
        foreach (var grid in grids)
        {
            if (grid.Name.Contains("Добавить", StringComparison.OrdinalIgnoreCase)) continue;
            var items = grid.FindAllDescendants(c => c.ByControlType(ControlType.DataItem));
            if (items.Length == 0)
                items = grid.FindAllDescendants(c => c.ByControlType(ControlType.ListItem));
            string? parent = null;
            foreach (var item in items)
            {
                var cells = item.FindAllChildren().Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                if (cells.Count == 0) cells.Add(item.Name);
                var code = cells.FirstOrDefault(c => c.Contains("СБЦ", StringComparison.OrdinalIgnoreCase)) ?? cells.ElementAtOrDefault(0) ?? "";
                var name = cells.FirstOrDefault(c => c != code && c.Length > 8) ?? "";
                decimal? a = cells.Select(ParseMoney).FirstOrDefault(v => v is not null);
                var row = AdeptUiRate.FromGrid("Базы ПИР", code, name, null, null, a, null, parent);
                if (!row.Qualifier) parent = code;
                list.Add(row);
            }
        }
        return list.Where(r => r.Code.Length > 0).ToList();
    }

    private static decimal? ParseMoney(string text)
    {
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c is ',' or '.').ToArray());
        return decimal.TryParse(cleaned.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    private static void Dump(AutomationElement el, int depth, int max)
    {
        if (depth > max) return;
        Console.WriteLine(new string(' ', depth * 2) + el.ControlType + " «" + el.Name + "»");
        if (el.Name.Contains("Добавить", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var child in el.FindAllChildren().Take(40))
            Dump(child, depth + 1, max);
    }

    private static Checkpoint? ReadCheckpoint(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path));
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private sealed record Checkpoint(string Title, string LastCode);
}
