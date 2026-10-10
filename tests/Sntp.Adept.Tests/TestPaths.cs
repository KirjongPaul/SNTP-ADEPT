namespace Sntp.Adept.Tests;

internal static class TestPaths
{
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sntp.Adept.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Не найден корень репозитория (Sntp.Adept.sln).");
    }

    public static string Sample(string name) => Path.Combine(RepoRoot(), "samples", name);

    public static string PilotExcel()
    {
        var local = Sample("pilot.xlsx");
        if (File.Exists(local)) return local;
        throw new FileNotFoundException("Нет samples/pilot.xlsx");
    }

    public static string V32Excel()
    {
        var sample = Sample("pilot-v32.xlsx");
        if (File.Exists(sample)) return sample;

        foreach (var dir in new[]
                 {
                     Path.Combine(RepoRoot(), "MD"),
                     @"d:\Download",
                     Path.Combine(RepoRoot(), "samples")
                 })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.xlsx"))
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith("~$")) continue;
                if (name.Contains("V3.2", StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }

        throw new FileNotFoundException("Нет книги Excel V3.2 (MD/*V3.2*.xlsx).");
    }
}
