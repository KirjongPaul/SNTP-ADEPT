using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;
using Sntp.Adept.Domain.Paste;
using Sntp.Adept.Export;

namespace Sntp.Adept.Tests;

public class PlanFeatureTests
{
    [Fact]
    public void Fgis_Periods_And_Empty_Registry()
    {
        var periods = FgisCsParser.ParsePeriods("""{"items":[{"value":426,"text":"2 квартал 2026 г."}],"activePeriodId":426}""");
        Assert.Equal(426, periods[0].Id);
        Assert.True(periods[0].Active);
        Assert.True(FgisCsParser.IsEmptyRegistry("""{"hierarchy":[],"documents":[]}"""));
        var sn = FgisCsParser.ParseSn("""{"documents":[{"name":"СБЦП 81-2001-17","approvingActInfo":"приказ"}]}""", "design", 426);
        Assert.Contains(sn, r => r.Name.Contains("81-2001-17"));
        var idx = FgisCsParser.ParseIndexes("""{"name":"2 квартал 2026 г.","indexes":[{"estimatedPriceSurveyDesignForBuildingIndex":1.53,"rtmPriceLevel":"2001"}]}""", 426);
        Assert.Contains(idx, r => r.Value == 1.53m);
    }

    [Fact]
    public void Paste_Classifies_Header_Section_And_Work()
    {
        var text = "Объект Степновская\n№\tНаименование\tОбоснование\n1\tДорога\tНЗ №849/пр табл. 3.8 п.10\nРаздел трассы\nИтого\t100";
        var preview = ClipboardGridParser.Parse(text);
        Assert.Equal("tsv", preview.Format);
        Assert.Contains(preview.Rows, r => r.Kind == PasteRowKind.ColumnMap);
        Assert.Contains(preview.Rows, r => r.Kind == PasteRowKind.Work);
        Assert.Contains(preview.Rows, r => r.Kind == PasteRowKind.Total);
        var positions = ClipboardGridParser.ToPositions(preview);
        Assert.Contains(positions, p => p.Kind == PositionKind.Work && p.CatalogCode == "NZ-849-3.8-10");
    }

    [Fact]
    public void Gge_Is_Rejected()
    {
        var xml = """<?xml version="1.0" encoding="utf-8"?><Construction></Construction>""";
        var ex = Assert.Throws<InvalidOperationException>(() => GrandXml.LoadXml(System.Text.Encoding.UTF8.GetBytes(xml), "estimate.gge"));
        Assert.Contains("gge-not-pir", ex.Message);
    }

    [Fact]
    public void WorkFolder_Copy_Delete_Rescan()
    {
        var root = Path.Combine(Path.GetTempPath(), "sntp-adept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Тест папка", Customer = "Газпром проектирование" });
            var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Локальная 1", Number = "1" });
            WorkFolder.Create(doc, root);
            Assert.True(File.Exists(Path.Combine(doc.Project.WorkFolder!, "project.json")));
            Assert.True(File.Exists(Path.Combine(doc.Project.WorkFolder!, "desktop.ini")));
            var ini = File.ReadAllText(Path.Combine(doc.Project.WorkFolder!, "desktop.ini"));
            Assert.Contains("LocalizedResourceName=Тест папка", ini);
            Assert.Contains("Сметы", File.ReadAllText(Path.Combine(doc.Project.WorkFolder!, "estimates", "desktop.ini")));
            Assert.DoesNotContain("vat", ini, StringComparison.OrdinalIgnoreCase);

            var copy = WorkFolder.CopyEstimate(doc, est.Id);
            WorkFolder.Rescan(doc);
            Assert.Equal(2, doc.Estimates.Count);
            Assert.True(WorkFolder.DeleteEstimate(doc, copy.Id));
            WorkFolder.Rescan(doc);
            Assert.Single(doc.Estimates);

            var loaded = WorkFolder.Load(doc.Project.WorkFolder!);
            Assert.Equal("Тест папка", loaded.Project.ObjectName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Append(root))
                {
                    var info = new DirectoryInfo(dir);
                    info.Attributes &= ~FileAttributes.ReadOnly;
                }
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void Section_Becomes_Its_Own_Chapter()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Главы" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { ChapterCaption = "Общее" });
        est.Positions.Add(new Position { Kind = PositionKind.Section, Name = "Трасса" });
        est.Positions.Add(new Position { Kind = PositionKind.Work, Name = "Участок", Code = "СБЦП19-5-1", PriceA = 1, Quantity = 1 });
        var xml = System.Text.Encoding.GetEncoding(1251).GetString(GrandXml.WriteUnsignedXml(doc));
        Assert.Contains("Caption=\"Трасса\"", xml);
        var back = GrandXml.LoadXml(System.Text.Encoding.GetEncoding(1251).GetBytes(xml), "chapters.xml");
        Assert.Contains(back.Estimates[0].Positions, p => p.Kind == PositionKind.Work && p.Code == "СБЦП19-5-1");
    }
}
