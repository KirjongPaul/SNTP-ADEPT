using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

/// <summary>
/// Нумерация как в Гранд-Смете, режим «по разделам дробная»:
/// раздел 1, строки 1.1 1.2, раздел 2, строки 2.1.
/// После строк раздела ставится промежуточный итог, в конце сметы — общий.
/// </summary>
public static class PositionOrder
{
    public static void MoveBefore(Estimate estimate, string positionId, string beforeId)
    {
        var from = estimate.Positions.FindIndex(p => p.Id == positionId);
        if (from < 0 || !IsBody(estimate.Positions[from])) return;

        var block = TakeBlock(estimate.Positions, from);
        if (block.Any(p => p.Id == beforeId))
        {
            estimate.Positions.InsertRange(from, block);
            return;
        }

        var insert = estimate.Positions.FindIndex(p => p.Id == beforeId);
        if (insert < 0 || !IsBody(estimate.Positions[insert]))
        {
            estimate.Positions.InsertRange(from, block);
            return;
        }

        estimate.Positions.InsertRange(insert, block);
        Arrange(estimate);
    }

    public static void MoveLast(Estimate estimate, string positionId)
    {
        var from = estimate.Positions.FindIndex(p => p.Id == positionId);
        if (from < 0 || !IsBody(estimate.Positions[from])) return;

        var block = TakeBlock(estimate.Positions, from);
        var last = estimate.Positions.FindLastIndex(IsBody);
        estimate.Positions.InsertRange(last < 0 ? 0 : last + 1, block);
        Arrange(estimate);
    }

    public static Position InsertSection(Estimate estimate, string? beforeId)
    {
        var section = new Position
        {
            Kind = PositionKind.Section,
            Name = "Раздел",
            Origin = OriginKind.Created,
            EstimateNumber = estimate.Number
        };
        var index = string.IsNullOrWhiteSpace(beforeId)
            ? FirstTail(estimate)
            : estimate.Positions.FindIndex(p => p.Id == beforeId);
        if (index < 0 || !IsBody(estimate.Positions[index]))
            index = FirstTail(estimate);
        estimate.Positions.Insert(index, section);
        Arrange(estimate);
        return section;
    }

    public static Position InsertBlank(Estimate estimate, string? beforeId)
    {
        var pos = new Position
        {
            Kind = PositionKind.Work,
            Name = "Новая позиция",
            Qty = 1m,
            Origin = OriginKind.Created,
            EstimateNumber = estimate.Number,
            SheetName = estimate.Sheet
        };
        var index = string.IsNullOrWhiteSpace(beforeId)
            ? FirstTail(estimate)
            : estimate.Positions.FindIndex(p => p.Id == beforeId);
        if (index < 0 || !IsBody(estimate.Positions[index]))
            index = FirstTail(estimate);
        estimate.Positions.Insert(index, pos);
        Arrange(estimate);
        return pos;
    }

    public static bool RemoveRow(Estimate estimate, string positionId)
    {
        var pos = estimate.Positions.FirstOrDefault(p => p.Id == positionId);
        if (pos is null) return false;
        if (pos.Kind == PositionKind.Section)
            return RemoveSection(estimate, positionId);
        if (pos.Kind is not (PositionKind.Work or PositionKind.Text))
            return false;
        estimate.Positions.Remove(pos);
        Arrange(estimate);
        return true;
    }

    public static bool RemoveSection(Estimate estimate, string sectionId)
    {
        var index = estimate.Positions.FindIndex(p => p.Id == sectionId && p.Kind == PositionKind.Section);
        if (index < 0) return false;
        estimate.Positions.RemoveAt(index);
        Arrange(estimate);
        return true;
    }

    public static void Arrange(Estimate estimate)
    {
        var pool = estimate.Positions.Where(p => p.Kind == PositionKind.SectionTotal).ToList();
        var grand = estimate.Positions.LastOrDefault(p => p.Kind == PositionKind.Total);
        var next = new List<Position>();
        Position? open = null;

        foreach (var pos in estimate.Positions)
        {
            if (pos.Kind is not (PositionKind.Work or PositionKind.Section or PositionKind.Text))
                continue;
            if (pos.Kind == PositionKind.Section)
            {
                if (open is not null)
                    next.Add(TakeSectionTotal(pool, open));
                next.Add(pos);
                open = pos;
            }
            else
            {
                next.Add(pos);
            }
        }

        if (open is not null)
            next.Add(TakeSectionTotal(pool, open));

        next.AddRange(estimate.Positions.Where(p => p.Kind == PositionKind.Accrual));

        grand ??= new Position { Kind = PositionKind.Total, Origin = OriginKind.Created };
        grand.Kind = PositionKind.Total;
        grand.Name = "Итого по смете";
        grand.Number = "";
        grand.EstimateNumber = estimate.Number;
        next.Add(grand);

        estimate.Positions.Clear();
        estimate.Positions.AddRange(next);
        Number(estimate);
    }

    public static void FillTotals(Estimate estimate)
    {
        decimal run = 0;
        foreach (var pos in estimate.Positions)
        {
            if (pos.Kind == PositionKind.Section)
                run = 0;
            else if (pos.Kind == PositionKind.Work)
                run += pos.EngineAmount ?? 0;
            else if (pos.Kind == PositionKind.SectionTotal)
            {
                pos.EngineAmount = Math.Round(run, 2, MidpointRounding.AwayFromZero);
                pos.Delta = null;
                run = 0;
            }
        }

        var grand = estimate.Positions.LastOrDefault(p => p.Kind == PositionKind.Total);
        if (grand is not null)
        {
            grand.EngineAmount = estimate.EngineAmount;
            grand.Delta = null;
        }
    }

    private static void Number(Estimate estimate)
    {
        var section = 0;
        var item = 0;
        var inSection = false;
        foreach (var pos in estimate.Positions)
        {
            if (pos.Kind == PositionKind.Section)
            {
                section++;
                item = 0;
                inSection = true;
                pos.Number = section.ToString();
            }
            else if (pos.Kind == PositionKind.Work)
            {
                item++;
                pos.Number = inSection ? $"{section}.{item}" : item.ToString();
                if (inSection)
                    pos.Section = CurrentSectionId(estimate, pos);
            }
            else if (pos.Kind == PositionKind.SectionTotal)
            {
                pos.Number = "";
                pos.Name = $"Итого по разделу {section}";
            }
        }
    }

    private static string? CurrentSectionId(Estimate estimate, Position work)
    {
        string? id = null;
        foreach (var pos in estimate.Positions)
        {
            if (pos.Kind == PositionKind.Section)
                id = pos.Id;
            if (pos.Id == work.Id)
                return id;
        }
        return id;
    }

    private static Position TakeSectionTotal(List<Position> pool, Position section)
    {
        var hit = pool.FirstOrDefault(p => p.Section == section.Id);
        if (hit is null)
        {
            hit = new Position
            {
                Kind = PositionKind.SectionTotal,
                Origin = OriginKind.Created,
                Section = section.Id,
                EstimateNumber = section.EstimateNumber
            };
        }
        else
        {
            pool.Remove(hit);
        }

        hit.Section = section.Id;
        hit.EstimateNumber = section.EstimateNumber;
        return hit;
    }

    private static List<Position> TakeBlock(List<Position> list, int from)
    {
        var row = list[from];
        var block = new List<Position> { row };
        list.RemoveAt(from);
        if (row.Kind != PositionKind.Section)
            return block;

        while (from < list.Count && list[from].Kind is PositionKind.Work or PositionKind.Text)
        {
            block.Add(list[from]);
            list.RemoveAt(from);
        }
        return block;
    }

    private static bool IsBody(Position pos) =>
        pos.Kind is PositionKind.Work or PositionKind.Section or PositionKind.Text;

    private static int FirstTail(Estimate estimate)
    {
        var index = estimate.Positions.FindIndex(p => !IsBody(p));
        return index < 0 ? estimate.Positions.Count : index;
    }
}
