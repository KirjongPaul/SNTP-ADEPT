using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class BookTests
{
    [Fact]
    public void Validator_Rejects_Negative_And_Overlap()
    {
        var book = new ReferenceBook
        {
            Id = "t",
            Title = "Проба",
            Tables =
            [
                new BookTable
                {
                    Code = "1",
                    Kind = "price",
                    Items =
                    [
                        new BookItem { N = "1", A = -1, Xmin = 0, Xmax = 5 },
                        new BookItem { N = "1", A = 2, Xmin = 3, Xmax = 8 }
                    ]
                }
            ]
        };
        var issues = BookValidator.Check(book);
        Assert.Contains(issues, s => s.Contains("отрицательный"));
        Assert.Contains(issues, s => s.Contains("пересекаются"));
        Assert.DoesNotContain(issues, s => s.Contains("дыра"));
    }

    [Fact]
    public void Validator_Warns_About_Gap_Between_Bands()
    {
        var book = new ReferenceBook
        {
            Id = "t",
            Title = "Проба",
            Tables =
            [
                new BookTable
                {
                    Code = "3.1",
                    Kind = "price",
                    Items =
                    [
                        new BookItem { N = "1", Name = "Газопровод", Xmin = 0, Xmax = 10 },
                        new BookItem { N = "1", Name = "Газопровод", Xmin = 10, Xmax = 40 },
                        new BookItem { N = "1", Name = "Газопровод", Xmin = 45, Xmax = 80 }
                    ]
                }
            ]
        };
        var issues = BookValidator.Check(book);
        Assert.Contains(issues, s => s.Contains("дыра между 40 и 45"));
        Assert.DoesNotContain(issues, s => s.Contains("пересекаются"));
    }

    [Fact]
    public void Validator_Does_Not_Call_Point_Steps_A_Gap()
    {
        var book = new ReferenceBook
        {
            Id = "t",
            Tables =
            [
                new BookTable
                {
                    Code = "3.3",
                    Kind = "price",
                    Items =
                    [
                        new BookItem { N = "1", Name = "Мощность", Xmin = 16, Xmax = 16 },
                        new BookItem { N = "1", Name = "Мощность", Xmin = 64, Xmax = 64 }
                    ]
                }
            ]
        };
        var issues = BookValidator.Check(book);
        Assert.DoesNotContain(issues, s => s.Contains("дыра"));
    }

    [Fact]
    public void Diff_Reports_Parameter_A()
    {
        var draft = Sample(10);
        var check = Sample(11);
        var diff = BookDiff.Compare(draft, check);
        Assert.Contains(diff, d => d.Path.EndsWith("/a") && d.Draft == "10" && d.Check == "11");
    }

    [Fact]
    public void Reconcile_Matches_Thousands_And_Flags_The_Rest()
    {
        var book = Sample(31.1m);
        book.Aliases.Add("849/пр");
        book.OrderYear = 2023;
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Сверка" });
        var ok = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Совпало" });
        ok.Positions.Add(Row("НЗ №849/пр Таблица 1. п.1 A = 31.1 тыс.руб; B = 2 тыс.руб; Xзад = 4 (га);", 31100m, 2000m, 4));
        var bad = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Чужое" });
        bad.Positions.Add(Row("СБЦ «Объекты связи», 2010г. табл.9 п.1", 1, 1, 1));
        var year = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Год" });
        year.Positions.Add(Row("НЗ №51/пр автомобильные дороги. 2025 г. Таблица 3.5 п.2", 1, 1, 1));

        var hits = BookReconciler.Check(doc, [book]);
        Assert.DoesNotContain(hits, h => h.PositionId == ok.Positions[0].Id && h.Status == "book-value-mismatch");
        Assert.Contains(hits, h => h.PositionId == bad.Positions[0].Id && h.Status == "book-not-entered");
        Assert.Contains(hits, h => h.PositionId == year.Positions[0].Id && h.Status == "book-ref-mismatch");
    }

    [Fact]
    public void Volumes_Stay_Split_By_Unit_And_Apply_One_Item()
    {
        var book = Sample(31.1m);
        book.Aliases.Add("849/пр");
        book.Rules.Add(new BookRule { Id = "range-select", Kind = "range-select", ClauseId = "13", Src = new BookSource { Page = 13 } });
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Объёмы" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Генплан" });
        est.Positions.Add(Row("НЗ №849/пр Таблица 1. п.1 Xзад = 4 (га);", 1, 1, 4));
        est.Positions.Add(Row("НЗ №912/пр Таблица 3.2 п.1 Xзад = 50 (тыс.куб.м.);", 1, 1, 50));
        est.Volumes = VolumeEngine.Derive(est, [book]);
        Assert.Equal(2, est.Volumes.Count);
        Assert.Contains(est.Volumes, v => v.Status == "book-not-entered");

        est.Volumes[0].Quantity = 6;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("updated", est.Volumes[0].Status);
        Assert.Equal(31100m, est.Positions[0].PriceA);
        Assert.Equal(6, est.Positions[0].Quantity);
        Assert.Contains("range-select", est.Volumes[0].Protocol[0]);
    }

    [Fact]
    public void Apply_Keeps_The_Row_When_Several_Items_Match()
    {
        var book = Sample(10);
        book.Tables[0].Items.Add(new BookItem { N = "1", Name = "Второй", Unit = "га", Xmin = 0, Xmax = 10, A = 3, B = 1 });
        book.Aliases.Add("849/пр");
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Группа" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Генплан" });
        est.Positions.Add(Row("НЗ №849/пр Таблица 1. п.1 Xзад = 4 (га);", 100m, 100m, 4));
        est.Volumes = VolumeEngine.Derive(est, [book]);
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("group", est.Volumes[0].Status);
        Assert.Equal(100m, est.Positions[0].PriceA);
    }

    [Fact]
    public void Volume_From_Book_Picks_The_Band_By_Quantity()
    {
        var book = Sample(10);
        book.Tables[0].Items.Add(new BookItem { N = "1", Name = "Площадка", Unit = "га", Xmin = 10, Xmax = 20, A = 40, B = 1 });
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Из сборника" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Генплан" });
        var volume = VolumeEngine.AddFromBook(est, book, "1", "1");
        Assert.Equal("Площадка", volume.Name);
        Assert.Equal("га", volume.Unit);
        volume.Quantity = 4;
        VolumeEngine.Apply(est, [book]);
        var row = Assert.Single(est.Positions, p => p.Kind == PositionKind.Work);
        Assert.Equal(10000m, row.PriceA);
        Assert.Equal(4, row.Quantity);
        Assert.Equal("updated", volume.Status);
    }

    [Fact]
    public void Quantity_Picks_Band_And_Names_The_Rule()
    {
        var book = Sample(31.1m);
        book.Tables[0].Items.Add(new BookItem { N = "1", Name = "Площадка", Unit = "га", Xmin = 10, Xmax = 20, A = 258.9m, B = 100.213m });
        book.Tables[0].Items.Add(new BookItem { N = "1", Name = "Площадка", Unit = "га", Xmin = 20, Xmax = 40, A = 1096.1m, B = 58.353m });
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Полоса" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Генплан" });
        var volume = VolumeEngine.AddFromBook(est, book, "1", "1");

        volume.Quantity = 20;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("within", volume.Pick!.Kind);
        Assert.Equal(10m, volume.Pick.Xmin);
        Assert.Equal(20m, volume.Pick.Xmax);
        Assert.Equal(258900m, est.Positions[0].PriceA);

        volume.Quantity = 0.2m;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("below-half", volume.Pick!.Kind);
        Assert.Equal(0.5m, volume.Pick.Xmin);
        Assert.Equal(31100m, est.Positions[0].PriceA);

        volume.Quantity = 50;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("above", volume.Pick!.Kind);
        Assert.Equal(40m, volume.Pick.Xmax);
        Assert.Equal(1096100m, est.Positions[0].PriceA);
    }

    [Fact]
    public void Quantity_In_A_Gap_Does_Not_Rewrite_The_Row()
    {
        var book = Sample(10);
        book.Tables[0].Items[0].Xmin = 0;
        book.Tables[0].Items[0].Xmax = 10;
        book.Tables[0].Items.Add(new BookItem { N = "1", Name = "Площадка", Unit = "га", Xmin = 10, Xmax = 40, A = 40, B = 1 });
        book.Tables[0].Items.Add(new BookItem { N = "1", Name = "Площадка", Unit = "га", Xmin = 45, Xmax = 80, A = 80, B = 1 });
        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Дыра" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Генплан" });
        var volume = VolumeEngine.AddFromBook(est, book, "1", "1");
        volume.Quantity = 4;
        VolumeEngine.Apply(est, [book]);
        var kept = est.Positions[0].PriceA;
        volume.Quantity = 42;
        VolumeEngine.Apply(est, [book]);
        Assert.Null(volume.Pick);
        Assert.Equal("group", volume.Status);
        Assert.Contains("дыра между 40 и 45", volume.Protocol[0]);
        Assert.Equal(kept, est.Positions[0].PriceA);
    }

    [Fact]
    public void Markdown_Table_Becomes_Items()
    {
        const string text = """
            от 28 ноября 2023 г. N 849/пр
            I. Общие положения
            13. Стоимость вне интервалов определяется методом интерполяции или экстраполяции.
            III. Параметры
            приведены в таблице 9.
            Таблица 9
            | N | Наименование объекта | Натуральный показатель "X" объекта | Границы интервалов | Параметры цены | |
            | --- | --- | --- | --- | --- | --- |
            | | | | | "а" | "в" |
            | 1 | 2 | 3 | 4 | 5 | 6 |
            | 1. | Установка мощностью | тыс. кВт | 16 | 2567,4 | |
            | 10. | Площадка | га | от 0,5 до 10 включительно | 31,1 | 122,993 |
            | | | га | от 10 до 20 включительно | 258,9 | 100,213 |
            1. Примечание к пункту 10 таблицы 9 о составе работ, которое длиннее двухсот сорока знаков и перечисляет: - переходы через дороги; - крановые узлы; - дома обходчиков и площадки обслуживания линейной части, чтобы обрезка цитаты была видна в проверке сразу.
            """;
        var book = NzMarkdown.Parse(text, "проба.txt");
        var issues = BookValidator.Check(book);
        Assert.Empty(issues);
        var table = Assert.Single(book.Tables, t => t.Code == "9");
        Assert.Equal(3, table.Items.Count);
        Assert.Equal(16m, table.Items[0].Xmin);
        Assert.Equal(16m, table.Items[0].Xmax);
        Assert.Equal(31.1m, table.Items[1].A);
        Assert.Equal(0.5m, table.Items[1].Xmin);
        Assert.Equal(10m, table.Items[1].Xmax);
        var note = Assert.Single(book.Notes, n => n.Anchor == "item:9/10");
        Assert.Contains("дома обходчиков", note.Quote);
        Assert.True(note.Quote.Length > 240);
        Assert.Contains(book.Rules, r => r.Kind == "interpolate-below-min");
    }

    [Fact]
    public void Official_Order_Parses_Table_3_8()
    {
        var path = @"C:\Users\anton\.cursor\projects\d-Program-Cursor-SNTP-ADEPT\agent-tools\94e9d71e-ca8b-48ae-a183-10804010904f.txt";
        if (!File.Exists(path)) return;
        var book = NzMarkdown.Parse(File.ReadAllText(path), "приказ-849-пр.txt");
        var issues = BookValidator.Check(book);
        Assert.True(issues.Count == 0, string.Join("\n", issues.Take(15)) + $"\nтаблиц {book.Tables.Count}, пунктов {book.Tables.Sum(t => t.Items.Count)}");
        var item = book.Tables.Single(t => t.Code == "3.8").Items.Single(i => i.N == "10" && i.Xmin == 0.5m);
        Assert.Equal(31.1m, item.A);
        Assert.Equal(122.993m, item.B);
        Assert.Equal(10m, item.Xmax);
        if (Environment.GetEnvironmentVariable("SNTP_EXPORT_BOOK") == "1")
            ExportBook(path, book);
    }

    [Fact]
    public void Stored_Notes_Keep_The_Whole_Footnote()
    {
        var root = Path.Combine(@"D:\NET\=SNTP-BOOKS", "НЗ 849-пр 2023 Газовая промышленность");
        var source = Path.Combine(root, "source", "приказ-849-пр.txt");
        var bookPath = Path.Combine(root, "book.json");
        if (!File.Exists(source) || !File.Exists(bookPath)) return;
        var fresh = NzMarkdown.Parse(File.ReadAllText(source), "приказ-849-пр.txt");
        var book = System.Text.Json.JsonSerializer.Deserialize<ReferenceBook>(File.ReadAllText(bookPath), Sntp.Adept.Domain.Json.PirJson.Options)
                   ?? throw new InvalidOperationException("Пустой book.json");
        var map = fresh.Notes.ToDictionary(n => n.LetterRef + "|" + n.Anchor, n => n.Quote);
        var changed = false;
        foreach (var note in book.Notes)
        {
            if (!map.TryGetValue(note.LetterRef + "|" + note.Anchor, out var quote) || quote == note.Quote) continue;
            note.Quote = quote;
            changed = true;
        }
        if (changed)
            File.WriteAllText(bookPath, System.Text.Json.JsonSerializer.Serialize(book, Sntp.Adept.Domain.Json.PirJson.Options));
        var sample = book.Notes.First(n => n.Anchor == "item:3.1/1" && n.LetterRef.Contains("примечание 4"));
        Assert.Contains("домов линейных обходчиков", sample.Quote);
    }

    private static void ExportBook(string sourcePath, ReferenceBook book)
    {
        var root = Path.Combine(@"D:\NET\=SNTP-BOOKS", "НЗ 849-пр 2023 Газовая промышленность");
        Directory.CreateDirectory(Path.Combine(root, "source"));
        Directory.CreateDirectory(Path.Combine(root, "letters"));
        Directory.CreateDirectory(Path.Combine(root, "work"));
        File.Copy(sourcePath, Path.Combine(root, "source", "приказ-849-пр.txt"), true);
        book.Status = "draft";
        var json = System.Text.Json.JsonSerializer.Serialize(book, Sntp.Adept.Domain.Json.PirJson.Options);
        File.WriteAllText(Path.Combine(root, "work", "draft.json"), json);
        File.WriteAllText(Path.Combine(root, "book.json"), json);
        File.WriteAllText(Path.Combine(root, "source", "sources.json"), """
            [
              {"file":"приказ-849-пр.txt","url":"https://pravo.ppt.ru/prikaz/minstroy/n-849-pr-290980","title":"Приказ Минстроя России от 28.11.2023 № 849/пр","frsn":"№849/пр от 28.11.2023 г.","frsnMatch":true}
            ]
            """);
    }

    [Fact]
    public void Excluded_Clause_Becomes_Estimate_Offers()
    {
        var book = new ReferenceBook
        {
            Id = "nz-849-2023",
            Title = "НЗ 849",
            GeneralPart =
            [
                new BookClause
                {
                    Id = "11",
                    Text = "Параметрами цены не учтены затраты на выполнение следующих работ: а) проектирование переходов через реки; б) проектирование домов обходчиков."
                }
            ]
        };
        var offers = EstimateOffers.From(book);
        Assert.Equal(2, offers.Count);
        Assert.Equal("nz-849-2023:11:0", offers[0].Id);
        Assert.Equal("а", offers[0].Mark);
        Assert.Equal("Переходы через реки", offers[0].ShortTitle);
        Assert.Contains("обходчиков", offers[1].Text);
        Assert.Equal(
            "Техническое обследование и сохранность зданий и сооружений",
            EstimateOffers.ShortName("по проведению технического обследования и разработки мероприятий по сохранности зданий и сооружений"));
        Assert.Equal(
            "Рекультивация нарушенных территорий",
            EstimateOffers.ShortName("по проектированию рекультивации нарушенных территорий"));
        Assert.Equal(
            "Охрана памятников археологии",
            EstimateOffers.ShortName("по подготовке мероприятий по охране памятников археологии"));
    }

    [Fact]
    public void Shared_Factors_Become_One_Variable_Across_Books()
    {
        var nz = BookWith("nz-849-2023", "НЗ 849", "сероводород", 1.2m);
        var sbc = BookWith("sbc-12-2001", "СБЦ 12", "содержание сероводорода", 1.15m);
        var onlyTable = new ReferenceBook
        {
            Id = "nz-849-2023",
            Tables =
            [
                new BookTable { Code = "3.1", Kind = "price" },
                new BookTable
                {
                    Code = "3.1.1",
                    Kind = "factor",
                    Items = [new BookItem { N = "3", Name = "Проектирование надземного магистрального газопровода", A = 1.5m }]
                }
            ]
        };

        var variables = FactorCatalog.Build([nz, sbc, onlyTable]);
        var h2s = Assert.Single(variables, v => v.Id == "hydrogenSulfide");
        Assert.Equal("Сероводород", h2s.Alias);
        Assert.Equal(2, h2s.Uses.Count);
        Assert.Contains(h2s.Uses, u => u.BookId == "nz-849-2023" && u.Value == 1.2m && u.PriceTables.Contains("3.8"));
        Assert.Contains(h2s.Uses, u => u.BookId == "sbc-12-2001" && u.BookMark == "12" && u.Value == 1.15m);
        Assert.DoesNotContain(variables, v => v.Name.Contains("надземн"));
    }

    private static ReferenceBook BookWith(string id, string title, string factorName, decimal value) => new()
    {
        Id = id,
        Title = title,
        Tables =
        [
            new BookTable { Code = "3.8", Kind = "price" },
            new BookTable
            {
                Code = "2.1",
                Kind = "factor",
                Items = [new BookItem { N = "2", Name = factorName, A = value }]
            }
        ]
    };

    [Fact]
    public void Volume_Name_Drops_The_Ui_Label()
    {
        const string header = "ОБЪЁМ РАБОТ п. 1. Магистральный газопровод диаметром до 500 мм протяженностью:";
        Assert.Equal(
            "Магистральный газопровод диаметром до 500 мм протяженностью",
            BookGroupNaming.From(header));
        Assert.Equal(
            "Магистральный газопровод диаметром до 500 мм протяженностью",
            BookGroupNaming.From("Объём работ п. 1. Магистральный газопровод диаметром до 500 мм протяженностью:"));
        Assert.Equal("Площадка", BookGroupNaming.From("Площадка"));
        Assert.Equal("До 500 мм. включительно", BookGroupNaming.From("До 500 мм. включительно"));
    }

    [Fact]
    public void Indicator_Group_Still_Picks_The_Band()
    {
        var book = Sample(31.1m);
        var group = Assert.Single(BookTableGroup.Split(book.Tables[0]));
        Assert.Equal("indicator", group.SelectKind);
        Assert.Equal("Площадка", group.VolumeName);
        Assert.False(group.IsLump(book.Tables[0].Items[0]));

        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Полоса" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Генплан" });
        var volume = VolumeEngine.AddFromBook(est, book, "1", "1");
        volume.Quantity = 4;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("within", volume.Pick!.Kind);
        Assert.Equal(31100m, est.Positions[0].PriceA);
        Assert.Equal(2000m, est.Positions[0].PriceB);
        Assert.Contains("range-select", volume.Protocol[0]);
    }

    [Fact]
    public void Lump_Object_Does_Not_Follow_The_Indicator()
    {
        var book = new ReferenceBook
        {
            Id = "nz-test",
            Title = "Проба",
            OrderRef = "№849/пр",
            Tables =
            [
                new BookTable
                {
                    Code = "3.1",
                    Kind = "price",
                    Items = [new BookItem { N = "3.1", Name = "До 500 мм. включительно", Unit = "объект", A = 15 }]
                }
            ]
        };
        var group = Assert.Single(BookTableGroup.Split(book.Tables[0]));
        Assert.Equal("lump", group.SelectKind);
        Assert.Equal("объект", group.IndicatorUnit);

        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Укрупнение" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Линейная" });
        var volume = VolumeEngine.AddFromBook(est, book, "3.1", "3.1");
        Assert.Equal("До 500 мм. включительно", volume.Name);
        volume.Quantity = 2;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("lump", volume.Pick!.Kind);
        Assert.Equal(15000m, est.Positions[0].PriceA);
        Assert.Null(est.Positions[0].PriceB);
        Assert.Contains("укрупнённо", volume.Protocol[0]);
        Assert.DoesNotContain("(A+B", volume.Protocol[0]);

        volume.Quantity = 80;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("lump", volume.Pick!.Kind);
        Assert.Equal(15000m, est.Positions[0].PriceA);
        Assert.Null(est.Positions[0].PriceB);
        Assert.Null(est.Positions[0].Xmin);
        Assert.Null(est.Positions[0].Xmax);
    }

    [Fact]
    public void Mixed_Group_Picks_Lump_Or_The_Band()
    {
        var name = "Магистральный газопровод диаметром до 500 мм протяженностью:";
        var book = new ReferenceBook
        {
            Id = "nz-test",
            Title = "Проба",
            OrderRef = "№849/пр",
            Rules = [new BookRule { Id = "range-select", Kind = "range-select", ClauseId = "13" }],
            Tables =
            [
                new BookTable
                {
                    Code = "3.1",
                    Kind = "price",
                    Items =
                    [
                        new BookItem { N = "1", Name = name, Unit = "объект", Xmax = 1, A = 10 },
                        new BookItem { N = "1", Name = name, Unit = "км.", Xmin = 1, Xmax = 5, A = 20, B = 3 }
                    ]
                }
            ]
        };
        var group = Assert.Single(BookTableGroup.Split(book.Tables[0]));
        Assert.Equal("mixed", group.SelectKind);
        Assert.Equal("Магистральный газопровод диаметром до 500 мм протяженностью", group.VolumeName);
        Assert.Equal("км.", group.IndicatorUnit);
        Assert.Equal(group.VolumeName, BookGroupNaming.From("ОБЪЁМ РАБОТ п. 1. " + name));

        var doc = DocumentFactory.CreateObject(new CreateObjectRequest { ObjectName = "Смешанная" });
        var est = DocumentFactory.CreateLocal(doc, new CreateLocalRequest { Title = "Линейная" });
        var volume = VolumeEngine.AddFromBook(est, book, "3.1", "1");
        Assert.Equal(group.VolumeName, volume.Name);
        Assert.Equal("км.", volume.Unit);

        volume.Quantity = 0.4m;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("lump", volume.Pick!.Kind);
        Assert.Equal(10000m, est.Positions[0].PriceA);
        Assert.Null(est.Positions[0].PriceB);
        Assert.Contains("укрупнённо", volume.Protocol[0]);
        Assert.DoesNotContain("range-select", volume.Protocol[0]);

        volume.Quantity = 3;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("within", volume.Pick!.Kind);
        Assert.Equal(20000m, est.Positions[0].PriceA);
        Assert.Equal(3000m, est.Positions[0].PriceB);
        Assert.Contains("range-select", volume.Protocol[0]);

        volume.Quantity = 0.4m;
        VolumeEngine.Apply(est, [book]);
        Assert.Equal("lump", volume.Pick!.Kind);
        Assert.Null(est.Positions[0].PriceB);
        Assert.Equal(10000m, est.Positions[0].PriceA);
    }

    [Fact]
    public void Group_Statement_Can_Be_Overridden()
    {
        var source = new BookTableGroup("3.1", "3.1", "До 500 мм. включительно",
            [new BookItem { N = "3.1", Name = "До 500 мм. включительно", Unit = "объект", A = 15 }]);
        var survey = new SurveySketch(source);
        Assert.Equal("lump", source.SelectKind);
        Assert.Equal("indicator", survey.SelectKind);
        Assert.StartsWith(source.VolumeName, survey.Statement());
        Assert.Contains("изысканий", survey.Statement());
    }

    private sealed class SurveySketch : BookTableGroup
    {
        public SurveySketch(BookTableGroup source)
            : base(source.Table, source.Item, source.SourceName, source.Bands)
        {
        }

        public override bool IsLump(BookItem item) => false;

        public override string Statement() => VolumeName + " — постановка изысканий задаётся отдельно";
    }

    [Fact]
    public void Codex_Link_Is_The_Direct_Intranet_Address()
    {
        Assert.Equal(
            "http://techexpert/docs/d?nd=1304344846",
            CodexLinks.Href(new CodexRef { Nd = "1304344846" }));
        Assert.Null(CodexLinks.Href(new CodexRef { Nd = "1304/пр" }));
        Assert.Null(CodexLinks.Href(null));
        var issues = BookValidator.Check(new ReferenceBook
        {
            Id = "t",
            Title = "Проба",
            Codex = new CodexRef { Nd = "не номер" }
        });
        Assert.Contains(issues, s => s.Contains("Кодекса"));
    }

    private static ReferenceBook Sample(decimal a) => new()
    {
        Id = "nz-test",
        Title = "Проба",
        OrderRef = "№849/пр",
        Tables =
        [
            new BookTable
            {
                Code = "1",
                Kind = "price",
                Items = [new BookItem { N = "1", Name = "Площадка", Unit = "га", Xmin = 0.5m, Xmax = 10, A = a, B = 2 }]
            }
        ]
    };

    private static Position Row(string raw, decimal a, decimal b, decimal x) => new()
    {
        Kind = PositionKind.Work,
        Name = raw.Length > 40 ? raw[..40] : raw,
        Basis = new Basis { Raw = raw, Kind = "NZ" },
        PriceA = a,
        PriceB = b,
        Quantity = x
    };
}
