using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Tests;

public class PositionOrderTests
{
    [Fact]
    public void Works_Inside_Section_Number_As_Fractions()
    {
        var est = new Estimate { Number = "1" };
        est.Positions.Add(new Position { Kind = PositionKind.Section, Name = "Раздел" });
        est.Positions.Add(Work("Строка"));
        est.Positions.Add(Work("Ещё"));
        PositionOrder.Arrange(est);

        Assert.Equal(["1", "1.1", "1.2", "Итого по разделу 1", "Итого по смете"],
            est.Positions.Select(Label).ToArray());
    }

    [Fact]
    public void Moving_Section_Takes_Its_Rows()
    {
        var est = new Estimate();
        var a = new Position { Kind = PositionKind.Section, Name = "А" };
        var a1 = Work("А1");
        var b = new Position { Kind = PositionKind.Section, Name = "Б" };
        var b1 = Work("Б1");
        est.Positions.AddRange([a, a1, b, b1]);
        PositionOrder.Arrange(est);
        PositionOrder.MoveBefore(est, b.Id, a.Id);

        Assert.Equal(["Б", "Б1", "А", "А1"],
            est.Positions.Where(p => p.Kind is PositionKind.Section or PositionKind.Work).Select(p => p.Name).ToArray());
        Assert.Equal("1.1", b1.Number);
        Assert.Equal("2.1", a1.Number);
        Assert.Equal("Итого по смете", est.Positions[^1].Name);
    }

    [Fact]
    public void Insert_Puts_Blank_Before_Selected_And_Delete_Drops_Only_That_Row()
    {
        var est = new Estimate();
        var keep = Work("Оставить");
        var drop = Work("Убрать");
        est.Positions.Add(keep);
        est.Positions.Add(drop);
        var blank = PositionOrder.InsertBlank(est, drop.Id);
        Assert.Equal(["Оставить", "Новая позиция", "Убрать"],
            est.Positions.Where(p => p.Kind == PositionKind.Work).Select(p => p.Name).ToArray());

        Assert.True(PositionOrder.RemoveRow(est, drop.Id));
        Assert.Contains(est.Positions, p => p.Id == keep.Id);
        Assert.Contains(est.Positions, p => p.Id == blank.Id);
        Assert.DoesNotContain(est.Positions, p => p.Id == drop.Id);
    }

    [Fact]
    public void Removing_Section_Keeps_Its_Rows()
    {
        var est = new Estimate();
        var section = new Position { Kind = PositionKind.Section, Name = "Раздел" };
        var first = Work("Первая");
        var second = Work("Вторая");
        est.Positions.AddRange([section, first, second]);
        PositionOrder.Arrange(est);
        Assert.True(PositionOrder.RemoveSection(est, section.Id));

        Assert.DoesNotContain(est.Positions, p => p.Kind == PositionKind.Section);
        Assert.Equal(["Первая", "Вторая"], est.Positions.Where(p => p.Kind == PositionKind.Work).Select(p => p.Name).ToArray());
        Assert.Equal(["1", "2"], new[] { first.Number, second.Number });
        Assert.DoesNotContain(est.Positions, p => p.Kind == PositionKind.SectionTotal);
    }

    [Fact]
    public void Drop_Past_The_Last_Row_Lands_Before_The_Tail()
    {
        var est = new Estimate();
        var first = Work("Первая");
        var last = Work("Последняя");
        est.Positions.Add(first);
        est.Positions.Add(last);
        est.Positions.Add(new Position { Kind = PositionKind.Accrual, Name = "НДС", Number = "7" });
        PositionOrder.MoveLast(est, first.Id);

        Assert.Equal(["Последняя", "Первая"], est.Positions.Where(p => p.Kind == PositionKind.Work).Select(p => p.Name).ToArray());
        Assert.Equal("НДС", est.Positions[^2].Name);
        Assert.Equal("Итого по смете", est.Positions[^1].Name);
    }

    [Fact]
    public void Section_Total_Sums_Its_Rows()
    {
        var est = new Estimate();
        est.Positions.Add(new Position { Kind = PositionKind.Section, Name = "А" });
        est.Positions.Add(Work("А1", 10m));
        est.Positions.Add(Work("А2", 2.5m));
        PositionOrder.Arrange(est);
        est.EngineAmount = 12.5m;
        PositionOrder.FillTotals(est);

        Assert.Equal(12.5m, est.Positions.Single(p => p.Kind == PositionKind.SectionTotal).EngineAmount);
        Assert.Equal(12.5m, est.Positions[^1].EngineAmount);
    }

    private static string Label(Position pos) =>
        pos.Kind is PositionKind.SectionTotal or PositionKind.Total ? pos.Name : pos.Number;

    private static Position Work(string name, decimal amount = 0) => new()
    {
        Kind = PositionKind.Work,
        Name = name,
        EngineAmount = amount
    };
}
