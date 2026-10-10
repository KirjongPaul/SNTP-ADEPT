using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class SourceCatalogTests
{
    [Fact]
    public void Add_Lists_Kind_And_Folder_Name()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adept-src-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var doc = new PirDocument
            {
                Id = "p",
                Project = new ProjectHeader { WorkFolder = dir, ObjectName = "Объект" }
            };
            var item = SourceCatalog.Add(doc, "Задание на проектирование.pdf", [1, 2, 3]);
            Assert.Equal("Задание", item.Kind);
            Assert.Equal("Программа изысканий", SourceCatalog.KindOf("Программа инженерных изысканий.docx"));
            Assert.Equal("Технические требования", SourceCatalog.KindOf("Технические требования.pdf"));
            Assert.Contains(SourceCatalog.List(doc), file => file.Name == "Задание на проектирование.pdf");
            var ini = File.ReadAllText(Path.Combine(dir, "sources", "desktop.ini"));
            Assert.Contains("Исходные данные", ini);
            SourceCatalog.Remove(doc, item.Name);
            Assert.Empty(SourceCatalog.List(doc));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                foreach (var folder in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).Append(dir))
                    ShellFolder.Release(folder);
                Directory.Delete(dir, true);
            }
        }
    }
}
