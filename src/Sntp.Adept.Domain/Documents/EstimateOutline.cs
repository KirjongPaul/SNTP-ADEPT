using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Documents;

/// <summary>Номера локальных смет выводятся из порядка и глубины, не хранятся как независимые коды.</summary>
public static class EstimateOutline
{
    public static void Renumber(PirDocument doc)
    {
        var n = 0;
        var k = 0;
        var m = 0;
        foreach (var est in doc.Estimates)
        {
            var depth = Math.Clamp(est.OutlineDepth, 0, 2);
            est.OutlineDepth = depth;
            if (depth == 0)
            {
                n++;
                k = 0;
                m = 0;
                est.Number = n.ToString();
            }
            else if (depth == 1)
            {
                if (n == 0) n = 1;
                k++;
                m = 0;
                est.Number = $"{n}.{k}";
            }
            else
            {
                if (n == 0) n = 1;
                if (k == 0) k = 1;
                m++;
                est.Number = $"{n}.{k}.{m}";
            }

            est.Sheet = est.Number;
            foreach (var pos in est.Positions)
                pos.EstimateNumber = est.Number;
        }
    }

    public static void Move(PirDocument doc, string estimateId, string? targetId, bool asChild)
    {
        var from = IndexOf(doc, estimateId);
        if (from < 0) throw new InvalidOperationException("Смета не найдена");
        var block = ExtractBlock(doc.Estimates, from);
        if (targetId is not null && block.Any(e => Same(e, targetId)))
        {
            doc.Estimates.InsertRange(from, block);
            return;
        }

        int insertAt;
        int rootDepth;
        if (asChild)
        {
            var parent = IndexOf(doc, targetId ?? "");
            if (parent < 0)
            {
                doc.Estimates.AddRange(block);
                Renumber(doc);
                return;
            }
            rootDepth = Math.Min(2, doc.Estimates[parent].OutlineDepth + 1);
            insertAt = parent + 1;
        }
        else if (string.IsNullOrWhiteSpace(targetId))
        {
            insertAt = doc.Estimates.Count;
            rootDepth = 0;
        }
        else
        {
            insertAt = IndexOf(doc, targetId);
            if (insertAt < 0) insertAt = doc.Estimates.Count;
            rootDepth = insertAt < doc.Estimates.Count ? doc.Estimates[insertAt].OutlineDepth : 0;
        }

        var delta = rootDepth - block[0].OutlineDepth;
        foreach (var est in block)
            est.OutlineDepth = Math.Clamp(est.OutlineDepth + delta, 0, 2);
        doc.Estimates.InsertRange(insertAt, block);
        Renumber(doc);
    }

    public static int CompareNumbers(string? a, string? b)
    {
        var pa = Parts(a);
        var pb = Parts(b);
        var n = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < n; i++)
        {
            var da = i < pa.Length ? pa[i] : -1;
            var db = i < pb.Length ? pb[i] : -1;
            var c = da.CompareTo(db);
            if (c != 0) return c;
        }
        return 0;
    }

    private static List<Estimate> ExtractBlock(List<Estimate> list, int from)
    {
        var depth = list[from].OutlineDepth;
        var block = new List<Estimate> { list[from] };
        list.RemoveAt(from);
        while (from < list.Count && list[from].OutlineDepth > depth)
        {
            block.Add(list[from]);
            list.RemoveAt(from);
        }
        return block;
    }

    /// <summary>
    /// Над выделенным блоком ставится новый раздел. Сметы блока вкладываются в него и разделами не становятся.
    /// </summary>
    public static Estimate Group(PirDocument doc, IReadOnlyList<string> ids, string? title)
    {
        var block = TakeContiguous(doc, ids);
        if (block.Any(e => e.OutlineDepth >= 2))
            throw new InvalidOperationException("Дальше второго уровня раздел не ставится.");
        var at = doc.Estimates.IndexOf(block[0]);
        foreach (var est in block)
            doc.Estimates.Remove(est);
        var heading = new Estimate
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Title = string.IsNullOrWhiteSpace(title) ? "Новый раздел" : title.Trim(),
            Heading = true,
            OutlineDepth = block[0].OutlineDepth,
            Form = EstimateForm.Unknown
        };
        foreach (var est in block)
            est.OutlineDepth++;
        doc.Estimates.Insert(at, heading);
        doc.Estimates.InsertRange(at + 1, block);
        Renumber(doc);
        return heading;
    }

    /// <summary>Выносит блок на уровень выше. Соседние невыделенные строки остаются у прежнего родителя.</summary>
    public static void Outdent(PirDocument doc, IReadOnlyList<string> ids)
    {
        var block = TakeContiguous(doc, ids);
        var depth = block[0].OutlineDepth;
        if (depth == 0)
            throw new InvalidOperationException("Строки уже на верхнем уровне.");
        var at = doc.Estimates.IndexOf(block[0]);
        foreach (var est in block)
            doc.Estimates.Remove(est);
        var parent = -1;
        for (var i = at - 1; i >= 0; i--)
        {
            if (doc.Estimates[i].OutlineDepth < depth)
            {
                parent = i;
                break;
            }
        }
        var insertAt = at;
        if (parent >= 0)
        {
            var parentDepth = doc.Estimates[parent].OutlineDepth;
            insertAt = parent + 1;
            while (insertAt < doc.Estimates.Count && doc.Estimates[insertAt].OutlineDepth > parentDepth)
                insertAt++;
        }
        foreach (var est in block)
            est.OutlineDepth--;
        doc.Estimates.InsertRange(insertAt, block);
        Renumber(doc);
    }

    public static void ReleaseChildren(PirDocument doc, Estimate heading)
    {
        var i = doc.Estimates.IndexOf(heading);
        if (i < 0) return;
        var depth = heading.OutlineDepth;
        i++;
        while (i < doc.Estimates.Count && doc.Estimates[i].OutlineDepth > depth)
        {
            doc.Estimates[i].OutlineDepth--;
            i++;
        }
    }

    private static List<Estimate> TakeContiguous(PirDocument doc, IReadOnlyList<string> ids)
    {
        if (ids is null || ids.Count == 0)
            throw new InvalidOperationException("Выделите строки свода.");
        var picked = new SortedSet<int>();
        foreach (var id in ids)
        {
            var i = IndexOf(doc, id);
            if (i < 0) throw new InvalidOperationException("Смета не найдена.");
            var depth = doc.Estimates[i].OutlineDepth;
            picked.Add(i);
            for (var j = i + 1; j < doc.Estimates.Count && doc.Estimates[j].OutlineDepth > depth; j++)
                picked.Add(j);
        }
        var list = picked.ToList();
        for (var k = 1; k < list.Count; k++)
        {
            if (list[k] != list[k - 1] + 1)
                throw new InvalidOperationException("Выделите подряд идущие строки.");
        }
        return list.Select(i => doc.Estimates[i]).ToList();
    }

    private static int IndexOf(PirDocument doc, string id) =>
        doc.Estimates.FindIndex(e => Same(e, id));

    private static bool Same(Estimate e, string id) =>
        string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(e.Number, id, StringComparison.OrdinalIgnoreCase);

    private static int[] Parts(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return [0];
        return number.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();
    }
}
