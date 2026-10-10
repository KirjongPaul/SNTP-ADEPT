using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Books;

public static class VolumeEngine
{
    public static List<WorkVolume> Derive(Estimate est, IReadOnlyList<ReferenceBook> books)
    {
        var list = new List<WorkVolume>();
        foreach (var pos in est.Positions.Where(p => p.Kind == PositionKind.Work))
        {
            var raw = pos.Basis?.Raw ?? "";
            var book = books.FirstOrDefault(b => BookReconciler.Mentions(b, raw));
            var unit = pos.QuantityUnit ?? UnitFrom(raw);
            var name = string.IsNullOrWhiteSpace(pos.Name) ? "Показатель" : pos.Name.Trim();
            var existing = list.FirstOrDefault(v =>
                string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(v.Unit ?? "", unit ?? "", StringComparison.OrdinalIgnoreCase)
                && v.BookId == book?.Id);
            if (existing is null)
            {
                existing = new WorkVolume
                {
                    Name = name,
                    Unit = unit,
                    Quantity = pos.Quantity,
                    BookId = book?.Id,
                    Table = BookText.TableCode(raw),
                    Item = BookText.ItemNo(raw),
                    Status = book is null && BookReconciler.LooksLikeBook(raw) ? "book-not-entered" : "bound"
                };
                list.Add(existing);
            }
            if (!existing.PositionIds.Contains(pos.Id)) existing.PositionIds.Add(pos.Id);
        }
        return list;
    }

    public static WorkVolume AddFromBook(Estimate est, ReferenceBook book, string tableCode, string itemNo)
    {
        var table = book.Tables.FirstOrDefault(t => t.Kind == "price" && string.Equals(t.Code, tableCode, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"В сборнике нет таблицы {tableCode}");
        var group = BookTableGroup.Split(table).FirstOrDefault(g => g.Item == BookText.NormItem(itemNo))
                    ?? throw new InvalidOperationException($"В таблице {table.Code} нет пункта {itemNo}");
        var already = est.Volumes.FirstOrDefault(v =>
            v.BookId == book.Id
            && string.Equals(v.Table, table.Code, StringComparison.OrdinalIgnoreCase)
            && BookText.NormItem(v.Item) == BookText.NormItem(itemNo));
        if (already is not null) return already;
        var volume = new WorkVolume
        {
            Name = group.VolumeName,
            Unit = group.IndicatorUnit,
            BookId = book.Id,
            Table = table.Code,
            Item = BookText.NormItem(itemNo),
            Status = "from-book"
        };
        est.Volumes.Add(volume);
        return volume;
    }

    public static void Apply(Estimate est, IReadOnlyList<ReferenceBook> books, string? onlyVolumeId = null)
    {
        foreach (var vol in est.Volumes)
        {
            if (onlyVolumeId is not null && vol.Id != onlyVolumeId) continue;
            vol.Protocol.Clear();
            vol.Candidates.Clear();
            vol.Pick = null;
            var positions = est.Positions.Where(p => vol.PositionIds.Contains(p.Id)).ToList();
            var book = books.FirstOrDefault(b => b.Id == vol.BookId)
                       ?? books.FirstOrDefault(b => positions.Any(p => BookReconciler.Mentions(b, p.Basis?.Raw ?? "")));
            if (book is null)
            {
                vol.Status = "book-not-entered";
                vol.Protocol.Add("Сборник ещё не введён, строки сметы не изменены.");
                continue;
            }
            vol.BookId = book.Id;
            var tableCode = vol.Table ?? positions.Select(p => BookText.TableCode(p.Basis?.Raw)).FirstOrDefault(s => s is not null);
            var table = book.Tables.FirstOrDefault(t => t.Kind == "price" && string.Equals(t.Code, tableCode, StringComparison.OrdinalIgnoreCase));
            if (table is null || vol.Quantity is not decimal x)
            {
                vol.Status = "book-not-entered";
                vol.Protocol.Add(table is null ? $"Таблица {tableCode ?? "—"} в сборнике не найдена." : "Количество не задано.");
                continue;
            }
            vol.Table = table.Code;
            if (positions.Count == 0)
            {
                var created = new Position
                {
                    Kind = PositionKind.Work,
                    Name = vol.Name,
                    Quantity = vol.Quantity,
                    QuantityUnit = vol.Unit,
                    Origin = OriginKind.Created,
                    EstimateNumber = est.Number,
                    Basis = new Basis
                    {
                        Kind = book.Kind,
                        Doc = book.OrderRef,
                        Table = table.Code,
                        Item = vol.Item,
                        Raw = $"{book.OrderRef}. Таблица {table.Code}. {vol.Name}, п.{vol.Item}"
                    }
                };
                est.Positions.Add(created);
                vol.PositionIds.Add(created.Id);
                positions.Add(created);
            }
            var cited = BookText.NormItem(vol.Item ?? positions.Select(p => BookText.ItemNo(p.Basis?.Raw)).FirstOrDefault(s => s is not null));
            if (cited.Length == 0)
            {
                vol.Status = "group";
                vol.Protocol.Add("Пункт сборника не указан. Строки не изменены.");
                continue;
            }
            var group = BookTableGroup.For(table, cited, vol.Name);
            if (group is null || group.Bands.Count == 0)
            {
                vol.Status = "group";
                vol.Protocol.Add($"В таблице {table.Code} нет пункта {cited}. Строки не изменены.");
                continue;
            }
            var bands = group.Bands;
            var ranged = bands.Where(i => i.Xmin is not null || i.Xmax is not null)
                .OrderBy(i => i.Xmin ?? decimal.MinValue)
                .ThenBy(i => i.Xmax ?? decimal.MaxValue)
                .ToList();
            if (Overlaps(ranged))
            {
                vol.Status = "group";
                vol.Protocol.Add($"Полосы п.{cited} пересекаются. Это ошибка ввода сборника. Строки не изменены.");
                continue;
            }
            var gap = GapAt(ranged, x);
            if (gap is not null)
            {
                vol.Status = "group";
                vol.Protocol.Add($"X={x} попал в {gap} пункта {cited}. Строки не изменены.");
                continue;
            }
            if (!TryPick(bands, ranged, x, out var one, out var kind))
            {
                vol.Status = "group";
                vol.Protocol.Add($"Для п.{cited} не выбрана одна расценка. Строки не изменены.");
                continue;
            }
            if (group.IsLump(one)) kind = "lump";
            vol.Item = BookText.NormItem(one.N);
            vol.Pick = new VolumePick
            {
                Item = vol.Item,
                Xmin = one.Xmin,
                Xmax = one.Xmax,
                Unit = one.Unit ?? vol.Unit,
                A = one.A,
                B = one.B,
                Kind = kind
            };
            vol.Status = "updated";
            var lump = group.IsLump(one);
            foreach (var pos in positions)
            {
                if (one.A is decimal a) pos.PriceA = BookText.ToRubles(a);
                if (lump)
                {
                    pos.PriceB = null;
                    pos.Xmin = null;
                    pos.Xmax = null;
                }
                else
                {
                    if (one.B is decimal b) pos.PriceB = BookText.ToRubles(b);
                    pos.Xmin = one.Xmin;
                    pos.Xmax = one.Xmax;
                }
                pos.Quantity = vol.Quantity;
                if (!string.IsNullOrWhiteSpace(vol.Unit)) pos.QuantityUnit = vol.Unit;
            }
            var rule = lump
                ? $"укрупнённо, единица «{one.Unit ?? "объект"}», величина показателя на расценку не влияет"
                : RuleFor(book, one, x);
            vol.Protocol.Add($"{book.OrderRef}, таблица {table.Code} п.{one.N}, {rule}. X={x} {vol.Unit}.");
        }
    }

    private static bool Overlaps(IReadOnlyList<BookItem> bands)
    {
        for (var i = 1; i < bands.Count; i++)
        {
            if (bands[i - 1].Xmax is decimal end && bands[i].Xmin is decimal start && start < end)
                return true;
        }
        return false;
    }

    private static string? GapAt(IReadOnlyList<BookItem> bands, decimal x)
    {
        for (var i = 1; i < bands.Count; i++)
        {
            if (bands[i - 1].Xmax is decimal end && bands[i].Xmin is decimal start && start > end && x > end && x < start)
            {
                if (bands[i - 1].Xmin == end && bands[i].Xmax == start)
                    continue;
                return $"дыра между {end} и {start}";
            }
        }
        return null;
    }

    private static bool TryPick(IReadOnlyList<BookItem> group, IReadOnlyList<BookItem> ranged, decimal x, out BookItem item, out string kind)
    {
        item = null!;
        kind = "";
        var inside = ranged.Where(i => BookText.ContainsX(i, x, ranged)).ToList();
        if (inside.Count == 1)
        {
            item = inside[0];
            kind = "within";
            return true;
        }
        if (inside.Count > 1) return false;
        if (ranged.Count == 0)
        {
            if (group.Count != 1) return false;
            item = group[0];
            kind = "within";
            return true;
        }
        var first = ranged[0];
        var last = ranged[^1];
        var min = ranged.Min(b => b.Xmin ?? b.Xmax ?? decimal.MaxValue);
        var max = ranged.Max(b => b.Xmax ?? b.Xmin ?? decimal.MinValue);
        if (x < min)
        {
            item = first;
            var edge = first.Xmin ?? min;
            kind = x < 0.5m * edge ? "below-half" : "below";
            return true;
        }
        if (x > max)
        {
            item = last;
            kind = "above";
            return true;
        }
        return false;
    }

    private static string RuleFor(ReferenceBook book, BookItem item, decimal x)
    {
        string kind;
        if (item.Xmin is decimal min && x < 0.5m * min) kind = "kpon-below-half";
        else if (item.Xmin is decimal min2 && x < min2) kind = "interpolate-below-min";
        else if (item.Xmax is decimal max && x > max) kind = "extrapolate-above-max";
        else kind = "range-select";
        var rule = book.Rules.FirstOrDefault(r => r.Kind == kind);
        var formula = kind switch
        {
            "interpolate-below-min" or "kpon-below-half" => "(A+B·(0,4·Xmin+0,6·X))·K",
            "extrapolate-above-max" => "(A+B·(0,4·Xmax+0,6·X))·K",
            _ => "(A+B·X)·K"
        };
        var page = rule?.Src?.Page is int p ? $", стр. {p}" : "";
        var clause = rule?.ClauseId is { Length: > 0 } c ? $", пункт {c}" : "";
        return $"правило {kind}, {formula}{clause}{page}";
    }

    private static string? UnitFrom(string raw)
    {
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"=\s*[\d\s.,]+\s*\(([^)]+)\)");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
