using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class AnalogTests
{
    [Fact]
    public void Lump_Band_Scores_Exact_Below_One_Kilometer()
    {
        var book = new ReferenceBook
        {
            Id = "nz-849",
            Aliases = ["849/пр"],
            OrderRef = "№849/пр",
            Tables =
            [
                new BookTable
                {
                    Code = "3.7",
                    Kind = "price",
                    Items =
                    [
                        new BookItem { N = "25", Name = "Сети", Unit = "объект", Xmax = 1, A = 3998.8m },
                        new BookItem { N = "25", Name = "Сети", Unit = "км.", Xmin = 1, Xmax = 5, A = 3051.8m, B = 946.944m }
                    ]
                }
            ]
        };
        var est = new Estimate { Id = "e" };
        est.Positions.Add(new Position
        {
            Kind = PositionKind.Work,
            Name = "Сети",
            Quantity = 0.5m,
            PriceA = 3_998_800m,
            Basis = new Basis { Raw = "НЗ №849/пр Таблица 3.7. п.25" }
        });
        var score = AnalogMatcher.ScoreOne(est, est.Positions[0], [book]);
        Assert.Equal(1, score.Confidence);
        Assert.Contains("3.7", score.Note);
    }

    [Fact]
    public void Changed_File_Is_Stale()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sntp-analog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = new PirDocument { Project = new ProjectHeader { WorkFolder = dir, ObjectName = "Проба" } };
            var card = AnalogCatalog.AddSource(doc, "смета.xlsx", [1, 2, 3]);
            Assert.False(AnalogCatalog.IsStale(doc, card));
            File.WriteAllBytes(AnalogCatalog.SourcePath(doc, card), [1, 2, 3, 4]);
            Assert.True(AnalogCatalog.IsStale(doc, card));
            Assert.True(File.Exists(Path.Combine(dir, "analogs", "desktop.ini")));
            Assert.Contains("Аналоги", File.ReadAllText(Path.Combine(dir, "analogs", "desktop.ini")));
            card.SystemName = "Генеральный план";
            AnalogCatalog.Touch(doc, card);
            var folderIni = File.ReadAllText(Path.Combine(dir, "analogs", card.Id, "desktop.ini"));
            Assert.Contains("LocalizedResourceName=Генеральный план", folderIni);
            var attrs = File.GetAttributes(Path.Combine(dir, "analogs", card.Id));
            Assert.True(attrs.HasFlag(FileAttributes.System));
            Assert.True(attrs.HasFlag(FileAttributes.ReadOnly));
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

    [Fact]
    public void Constraint_Caption_Is_Not_Stage()
    {
        var pos = new Position
        {
            Factors = [new Factor { Identifier = "Кст", Caption = "коэффициент стесненности", Role = "stage" }]
        };
        AnalogMatcher.Apply([pos], new AnalogMappings());
        Assert.Equal("constraint", pos.Factors[0].Role);
    }
}
