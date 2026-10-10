using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class OutlineTests
{
    [Fact]
    public void Depths_Become_1_1_1_1_2_2_1()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Нумерация" });
        Add(doc, 0, "Заголовок");
        Add(doc, 2, "Вложенная");
        Add(doc, 0, "Вторая");
        Add(doc, 1, "Под второй");
        EstimateOutline.Renumber(doc);
        DocumentFactory.RebuildSummary(doc);

        Assert.Equal(["1", "1.1.1", "2", "2.1"], doc.Estimates.Select(e => e.Number).ToArray());
        Assert.All(doc.Estimates, e => Assert.Equal(e.Number, e.Positions[0].EstimateNumber));
        Assert.Equal(doc.Estimates.Select(e => e.Number), doc.Summary!.Rows.Select(r => r.Code));
        Assert.Equal(doc.Estimates.Select(e => e.Id), doc.Summary.Rows.Select(r => r.Ref));
    }

    [Fact]
    public void Group_Takes_Next_Number_And_Leaves_Estimate_As_Estimate()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Раздел" });
        var before = Add(doc, 0, "До раздела");
        var first = Add(doc, 0, "Первая в группе");
        var second = Add(doc, 0, "Вторая в группе");
        EstimateOutline.Renumber(doc);

        var heading = EstimateOutline.Group(doc, [first.Id, second.Id], "Название");
        DocumentFactory.RebuildSummary(doc);

        Assert.False(before.Heading);
        Assert.False(first.Heading);
        Assert.False(second.Heading);
        Assert.True(heading.Heading);
        Assert.Equal("Название", heading.Title);
        Assert.Equal(["1", "2", "2.1", "2.2"], doc.Estimates.Select(e => e.Number).ToArray());
        Assert.Equal([before.Id, heading.Id, first.Id, second.Id], doc.Estimates.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Group_Above_Nested_Estimate_Does_Not_Promote_Parent()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Вложение" });
        var parent = Add(doc, 0, "Родитель");
        var child = Add(doc, 1, "Вложенная");
        EstimateOutline.Renumber(doc);

        var heading = EstimateOutline.Group(doc, [child.Id], null);

        Assert.False(parent.Heading);
        Assert.False(child.Heading);
        Assert.True(heading.Heading);
        Assert.Equal(["1", "1.1", "1.1.1"], doc.Estimates.Select(e => e.Number).ToArray());
        Assert.Equal([parent.Id, heading.Id, child.Id], doc.Estimates.Select(e => e.Id).ToArray());
    }

    [Fact]
    public void Outdent_Keeps_Sibling_Under_Previous_Parent()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Вынести" });
        var parent = Add(doc, 0, "Раздел");
        parent.Heading = true;
        var first = Add(doc, 1, "Первая");
        var second = Add(doc, 1, "Вторая");
        EstimateOutline.Renumber(doc);

        EstimateOutline.Outdent(doc, [first.Id]);

        Assert.True(parent.Heading);
        Assert.False(first.Heading);
        Assert.Equal([parent.Id, second.Id, first.Id], doc.Estimates.Select(e => e.Id).ToArray());
        Assert.Equal(["1", "1.1", "2"], doc.Estimates.Select(e => e.Number).ToArray());
    }

    [Fact]
    public void Group_Rejects_A_Gap_In_The_Selection()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Дыра" });
        var a = Add(doc, 0, "А");
        Add(doc, 0, "Б");
        var c = Add(doc, 0, "В");
        EstimateOutline.Renumber(doc);

        var ex = Assert.Throws<InvalidOperationException>(() => EstimateOutline.Group(doc, [a.Id, c.Id], null));
        Assert.Equal("Выделите подряд идущие строки.", ex.Message);
        Assert.Equal(["1", "2", "3"], doc.Estimates.Select(e => e.Number).ToArray());
    }

    [Fact]
    public void Deleting_Heading_Drops_Child_Indexes_And_Renumbers()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Снятие раздела" });
        var before = Add(doc, 0, "До");
        var heading = Add(doc, 0, "Раздел");
        heading.Heading = true;
        var first = Add(doc, 1, "Первая");
        var second = Add(doc, 1, "Вторая");
        var after = Add(doc, 0, "После");
        EstimateOutline.Renumber(doc);
        Assert.Equal(["1", "2", "2.1", "2.2", "3"], doc.Estimates.Select(e => e.Number).ToArray());

        EstimateOutline.ReleaseChildren(doc, heading);
        doc.Estimates.Remove(heading);
        EstimateOutline.Renumber(doc);

        Assert.Equal([before.Id, first.Id, second.Id, after.Id], doc.Estimates.Select(e => e.Id).ToArray());
        Assert.Equal(["1", "2", "3", "4"], doc.Estimates.Select(e => e.Number).ToArray());
        Assert.Equal(0, first.OutlineDepth);
        Assert.Equal(0, second.OutlineDepth);
        Assert.False(first.Heading);
        Assert.All(doc.Estimates, e => Assert.Equal(e.Number, e.Positions[0].EstimateNumber));
    }

    [Fact]
    public void Deleting_Nested_Heading_Keeps_One_Index_Level()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Вложенный раздел" });
        var parent = Add(doc, 0, "Раздел");
        parent.Heading = true;
        var sub = Add(doc, 1, "Подраздел");
        sub.Heading = true;
        var child = Add(doc, 2, "Внутри");
        var sibling = Add(doc, 1, "Рядом");
        EstimateOutline.Renumber(doc);

        EstimateOutline.ReleaseChildren(doc, sub);
        doc.Estimates.Remove(sub);
        EstimateOutline.Renumber(doc);

        Assert.Equal([parent.Id, child.Id, sibling.Id], doc.Estimates.Select(e => e.Id).ToArray());
        Assert.Equal(["1", "1.1", "1.2"], doc.Estimates.Select(e => e.Number).ToArray());
        Assert.Equal(1, child.OutlineDepth);
    }

    [Fact]
    public void Move_Renumbers_And_Title_Stays_On_Estimate()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Порядок" });
        var a = Add(doc, 0, "Альфа");
        Add(doc, 0, "Бета");
        EstimateOutline.Renumber(doc);
        EstimateOutline.Move(doc, a.Id, doc.Estimates[1].Id, asChild: true);
        Assert.Equal("1", doc.Estimates[0].Number);
        Assert.Equal("1.1", doc.Estimates[1].Number);
        Assert.Equal("Альфа", doc.Estimates[1].Title);
    }

    [Fact]
    public void Automation_Has_Parts_And_No_Denomination()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "АСУ" });
        var est = DocumentFactory.CreateAutomation(doc);
        Assert.Equal(EstimateForm.AsutpCard, est.Form);
        Assert.Equal(["ТЗ", "ОР", "ОО", "ИО", "ТО", "МО", "ПО"], est.Positions.Select(p => p.Name).ToArray());
        Assert.Equal("СБЦП 81-2001-22", est.Asutp!.Catalog);
        Assert.DoesNotContain(est.Factors, f => f.ProjectDoc == 0.001m || f.WorkDoc == 0.001m || f.Identifier == "Кд");
        Assert.Contains(est.Factors, f => f.Identifier == "Ки1");
        Assert.Equal("1", est.Number);
    }

    [Fact]
    public void Form3P_Is_Next_Number()
    {
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Труд" });
        var est = DocumentFactory.CreateForm3P(doc);
        Assert.Equal(EstimateForm.Form3P, est.Form);
        Assert.NotNull(est.Labor);
        Assert.Equal("1", est.Number);
    }

    [Fact]
    public void New_Object_Lands_Under_Catalog_Root()
    {
        var root = Path.Combine(Path.GetTempPath(), "sntp-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ensured = WorkCatalog.Ensure(root);
            Assert.Equal(root, ensured);
            var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Объект" });
            WorkFolder.Create(doc, root);
            Assert.StartsWith(root, doc.Project.WorkFolder, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(doc.Project.WorkFolder!, "project.json")));
            var listed = WorkCatalog.Scan(root);
            Assert.Contains(listed, p => p.Id == doc.Id);
        }
        finally
        {
            DropCatalog(root);
        }
    }

    [Fact]
    public void Catalog_Lists_Designer_And_Distinct_Estimate_Authors()
    {
        var root = Path.Combine(Path.GetTempPath(), "sntp-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Авторы", Designer = " ООО «Проект» " });
            Add(doc, 0, "Первая").Author = "Иванов";
            Add(doc, 0, "Вторая").Author = " иванов ";
            Add(doc, 0, "Третья").Author = "Петрова";
            Add(doc, 0, "Без автора");
            EstimateOutline.Renumber(doc);
            WorkFolder.Create(doc, root);

            var item = Assert.Single(WorkCatalog.Scan(root));
            Assert.Equal("ООО «Проект»", item.Designer);
            Assert.Equal(4, item.EstimateCount);
            Assert.Equal(["Иванов", "Петрова"], item.Authors.ToArray());
            Assert.Null(item.Total);
        }
        finally
        {
            DropCatalog(root);
        }
    }

    [Fact]
    public void Catalog_Sorts_Outline_And_Sums_Amounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "sntp-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Сводка" });
            var parent = Add(doc, 0, "Родитель");
            var later = Add(doc, 1, "Поздняя");
            var earlier = Add(doc, 1, "Ранняя");
            var empty = Add(doc, 0, "Пустая");
            parent.Number = "1";
            parent.EngineAmount = 100;
            later.Number = "1.10";
            later.OutlineDepth = 1;
            later.EngineAmount = 10;
            earlier.Number = "1.2";
            earlier.OutlineDepth = 1;
            earlier.ExcelAmount = 5;
            empty.Number = "2";
            WorkFolder.Create(doc, root);

            var item = Assert.Single(WorkCatalog.Scan(root));
            Assert.Equal(["1", "1.2", "1.10", "2"], item.Estimates.Select(e => e.Number).ToArray());
            Assert.Equal(["Родитель", "Ранняя", "Поздняя", "Пустая"], item.Estimates.Select(e => e.Title).ToArray());
            Assert.Equal(115m, item.Total);
            Assert.Equal(1, item.Estimates[1].Depth);
            Assert.Equal(5m, item.Estimates[1].Amount);
            Assert.Null(item.Estimates[3].Amount);
        }
        finally
        {
            DropCatalog(root);
        }
    }

    [Fact]
    public void Import_Estimate_Into_Another_Project_Gets_New_Ids()
    {
        var root = Path.Combine(Path.GetTempPath(), "sntp-catalog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Источник" });
            var est = Add(source, 0, "Дороги");
            var posId = est.Positions[0].Id;
            WorkFolder.Create(source, root);
            var target = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Приёмник" });
            Add(target, 0, "Своя");
            WorkFolder.Create(target, root);

            var loadedSrc = WorkFolder.Load(source.Project.WorkFolder!);
            var loadedTgt = WorkFolder.Load(target.Project.WorkFolder!);
            var srcEst = Assert.Single(loadedSrc.Estimates, e => e.Title == "Дороги");
            WorkFolder.ImportEstimate(loadedTgt, srcEst);

            Assert.Equal(2, loadedTgt.Estimates.Count);
            var copy = Assert.Single(loadedTgt.Estimates, e => e.Title == "Дороги");
            Assert.NotEqual(srcEst.Id, copy.Id);
            Assert.DoesNotContain(copy.Positions, p => p.Id == posId);
        }
        finally
        {
            DropCatalog(root);
        }
    }

    private static void DropCatalog(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Append(root))
            new DirectoryInfo(dir).Attributes &= ~FileAttributes.ReadOnly;
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(root, true);
    }

    private static Estimate Add(PirDocument doc, int depth, string title)
    {
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = title });
        est.OutlineDepth = depth;
        est.Positions.Add(new Position { Name = title, Kind = PositionKind.Work });
        return est;
    }
}
