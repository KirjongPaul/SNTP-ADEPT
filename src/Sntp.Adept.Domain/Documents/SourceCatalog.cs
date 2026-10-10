using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

public sealed class SourceFileItem
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Modified { get; set; } = "";
    public string Kind { get; set; } = "";
}

/// <summary>Папка исходных данных проекта. Файлы не разбираются и в смету не попадают.</summary>
public static class SourceCatalog
{
    public static string Ensure(PirDocument doc)
    {
        var dir = Path.Combine(WorkFolder.Require(doc), "sources");
        Directory.CreateDirectory(dir);
        WriteIni(dir);
        return dir;
    }

    public static List<SourceFileItem> List(PirDocument doc)
    {
        var dir = Ensure(doc);
        return Directory.GetFiles(dir)
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Where(file => (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(ToItem)
            .ToList();
    }

    public static SourceFileItem Add(PirDocument doc, string fileName, byte[] bytes)
    {
        var dir = Ensure(doc);
        var name = Safe(fileName);
        var path = Path.Combine(dir, name);
        if (File.Exists(path)) File.SetAttributes(path, FileAttributes.Normal);
        File.WriteAllBytes(path, bytes);
        return ToItem(new FileInfo(path));
    }

    public static void Remove(PirDocument doc, string fileName)
    {
        var path = Path.Combine(Ensure(doc), Safe(fileName));
        if (!File.Exists(path)) return;
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    public static string PathOf(PirDocument doc, string fileName)
    {
        var path = Path.Combine(Ensure(doc), Safe(fileName));
        if (!File.Exists(path)) throw new FileNotFoundException(fileName);
        return path;
    }

    public static string KindOf(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        if (name.Contains("задан")) return "Задание";
        if (name.Contains("изыск") || name.Contains("програм")) return "Программа изысканий";
        if (name.Contains("требован") || name.Contains("техн")) return "Технические требования";
        var ext = Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? "Документ" : ext;
    }

    private static SourceFileItem ToItem(FileInfo file) => new()
    {
        Name = file.Name,
        Size = file.Length,
        Modified = file.LastWriteTimeUtc.ToString("o"),
        Kind = KindOf(file.Name)
    };

    private static string Safe(string fileName)
    {
        var name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName);
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) name = "file.bin";
        return name;
    }

    private static void WriteIni(string dir)
    {
        ShellFolder.ShowAs(dir, "Исходные данные", "Задание, технические требования, программа изысканий и прочие документы для расчёта стоимости");
    }
}
