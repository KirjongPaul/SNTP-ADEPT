using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Excel;

public static class ExcelImporter
{
    public static PirDocument Import(string path, Action<ImportProgress>? report = null)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Import(fs, Path.GetFileName(path), report);
    }

    public static PirDocument Import(Stream stream, string fileName, Action<ImportProgress>? report = null)
    {
        void Report(int percent, string phase, string message, string? sheet = null, string? form = null,
            int? index = null, int? count = null) =>
            report?.Invoke(new ImportProgress
            {
                Percent = Math.Clamp(percent, 0, 100),
                Phase = phase,
                Message = message,
                Sheet = sheet,
                Form = form,
                SheetIndex = index,
                SheetCount = count
            });

        Report(1, "open", $"Открытие книги «{fileName}»");

        byte[]? hashBytes = null;
        if (stream.CanSeek)
        {
            Report(3, "open", "Контрольная сумма SHA-256");
            using var sha = SHA256.Create();
            hashBytes = sha.ComputeHash(stream);
            stream.Position = 0;
        }

        Report(6, "open", "Чтение листов Excel");
        var sheets = SheetTable.LoadWorkbook(stream);
        var n = sheets.Count;
        Report(10, "open", $"Книга открыта · {n} лист(ов)", count: n);

        var doc = new PirDocument
        {
            Source = new SourceInfo
            {
                File = fileName,
                Kind = "excel",
                Hash = hashBytes is null ? null : "sha256:" + Convert.ToHexString(hashBytes).ToLowerInvariant()
            },
            Project = new ProjectHeader
            {
                Stage = DesignStage.WorkDoc,
                StageLabel = "Рабочая документация",
                VatRate = 0.22m,
                KponPolicy = KponPolicy.AutoKpon
            }
        };

        for (var i = 0; i < n; i++)
        {
            var sheet = sheets[i];
            var form = DialectDetector.Detect(sheet);
            var label = ImportProgress.FormLabel(form);
            var pct = 10 + (int)Math.Round(72.0 * i / Math.Max(n, 1));
            Report(pct, "sheet", $"лист «{sheet.Name}» · {label}", sheet.Name, label, i + 1, n);
            switch (form)
            {
                case EstimateForm.Form1Ps:
                    ParseSvod(sheet, doc);
                    break;
                case EstimateForm.Form2P707:
                    doc.Estimates.Add(Parse2P707(sheet, doc));
                    break;
                case EstimateForm.AsutpCard:
                    doc.Estimates.Add(AsutpParser.Parse(sheet, doc));
                    break;
                case EstimateForm.Form2PSbc:
                    doc.Estimates.Add(Parse2PSbc(sheet, doc));
                    break;
                case EstimateForm.Form3P:
                case EstimateForm.LaborCost:
                    doc.Estimates.Add(Form3PParser.Parse(sheet, doc));
                    break;
                default:
                    if (sheet.Rows > 2)
                    {
                        doc.Issues.Add(new Clarification
                        {
                            Severity = ClarificationSeverity.Warning,
                            Code = "import",
                            Message = $"Лист «{sheet.Name}»: диалект не распознан"
                        });
                    }
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(doc.Project.ObjectName))
            doc.Project.ObjectName = Path.GetFileNameWithoutExtension(fileName);

        Report(84, "recalc", "Индекс объекта и пересчёт движком");
        ApplyProjectIndexFromPositions(doc);
        CalcEngine.Recalculate(doc);
        Report(88, "recalc", "Пересчёт завершён");
        return doc;
    }

    internal static void ParseSvod(SheetTable s, PirDocument doc)
    {
        doc.Project.ObjectName = FirstNonEmpty(s, 5, 3, 5, 0) ?? doc.Project.ObjectName;
        var stageText = FirstNonEmpty(s, 8, 3, 8, 0);
        if (!string.IsNullOrWhiteSpace(stageText) && !stageText.Contains("Наименование", StringComparison.Ordinal))
        {
            doc.Project.Stage = StageHelper.FromLabel(stageText);
            doc.Project.StageLabel = stageText.Trim();
        }
        doc.Project.Designer = FirstNonEmpty(s, 13, 3) ?? doc.Project.Designer;
        doc.Project.Customer = FirstNonEmpty(s, 15, 3) ?? doc.Project.Customer;

        for (var r = 0; r < Math.Min(s.Rows, 25); r++)
        {
            var t = s.Display(r, 0) + " " + s.Display(r, 1) + " " + s.Display(r, 3);
            if (t.Contains("Этап", StringComparison.OrdinalIgnoreCase) && t.Length < 160
                && !t.Contains("Наименование", StringComparison.OrdinalIgnoreCase))
                doc.Project.Phase = s.Display(r, 1).Length > 0 ? s.Display(r, 1) : t.Trim();
        }

        var summary = new Summary1Ps();
        for (var r = 0; r < s.Rows; r++)
        {
            var code = s.Display(r, 0).Trim();
            var title = s.Display(r, 1).Trim();
            if (title.Length == 0) title = s.Display(r, 3).Trim();

            if (Regex.IsMatch(code, @"^\d+\.\d+$")
                && !title.Contains("Итого", StringComparison.OrdinalIgnoreCase))
            {
                summary.Rows.Add(new SummaryRow
                {
                    Code = code,
                    Title = title,
                    Ref = s.Display(r, 4).Trim(),
                    Survey = Math.Round(s.Number(r, 5) ?? 0, 2, MidpointRounding.AwayFromZero),
                    Design = Math.Round(s.Number(r, 6) ?? 0, 2, MidpointRounding.AwayFromZero),
                    Total = Math.Round(s.Number(r, 7) ?? s.Number(r, 6) ?? 0, 2, MidpointRounding.AwayFromZero)
                });
            }

            if (title.Contains("Кроме того, НДС", StringComparison.OrdinalIgnoreCase))
            {
                var rate = s.Number(r, 4);
                if (rate is > 0 and < 1) doc.Project.VatRate = rate.Value;
                else if (rate is >= 1 and <= 100) doc.Project.VatRate = rate.Value / 100m;
                summary.Totals.Vat = Math.Round(s.Number(r, 6) ?? s.Number(r, 7) ?? 0, 2, MidpointRounding.AwayFromZero);
            }
            if (title.Contains("Итого по сводной", StringComparison.OrdinalIgnoreCase)
                || title.Contains("Итого Рабочая", StringComparison.OrdinalIgnoreCase)
                || title.StartsWith("Итого ", StringComparison.OrdinalIgnoreCase) && code.StartsWith("1."))
            {
                var d = s.Number(r, 6) ?? s.Number(r, 7);
                if (d is decimal dv)
                    summary.Totals.Design = Math.Round(dv, 2, MidpointRounding.AwayFromZero);
            }
            if (title.Contains("Всего по сводной", StringComparison.OrdinalIgnoreCase))
                summary.Totals.Grand = Math.Round(s.Number(r, 6) ?? s.Number(r, 7) ?? 0, 2, MidpointRounding.AwayFromZero);

            if (title.Contains("Примечание", StringComparison.OrdinalIgnoreCase)
                || s.Display(r, 0).Contains("Примечание", StringComparison.OrdinalIgnoreCase))
                summary.Note = (s.Display(r, 0) + " " + title).Trim();
        }

        if (summary.Totals.Design == 0 && summary.Rows.Count > 0)
            summary.Totals.Design = Math.Round(summary.Rows.Sum(x => x.Design), 2, MidpointRounding.AwayFromZero);
        if (summary.Totals.Vat == 0)
            summary.Totals.Vat = Math.Round(summary.Totals.Design * doc.Project.VatRate, 2, MidpointRounding.AwayFromZero);
        if (summary.Totals.Grand == 0)
            summary.Totals.Grand = Math.Round(summary.Totals.Design + summary.Totals.Vat, 2, MidpointRounding.AwayFromZero);

        summary.ExcelTotals = new SummaryTotals
        {
            Design = summary.Totals.Design,
            Vat = summary.Totals.Vat,
            Grand = summary.Totals.Grand,
            Survey = summary.Totals.Survey
        };
        doc.Summary = summary;
    }

    internal static Estimate Parse2P707(SheetTable s, PirDocument doc)
    {
        var est = new Estimate
        {
            Form = EstimateForm.Form2P707,
            Sheet = s.Name,
            Factors = [DocumentFactory.DefaultKi1()]
        };
        est.Title = FirstNonEmpty(s, 5, 5, 5, 0) ?? s.Name;
        est.Number = ExtractSmetaNumber(s) ?? GuessNumberFromSheet(s.Name);
        est.Id = est.Number ?? s.Name;
        if (string.IsNullOrWhiteSpace(doc.Project.Designer))
            doc.Project.Designer = FirstNonEmpty(s, 7, 5) ?? doc.Project.Designer;
        if (string.IsNullOrWhiteSpace(doc.Project.Customer))
            doc.Project.Customer = FirstNonEmpty(s, 9, 5) ?? doc.Project.Customer;

        string? section = null;
        Position? current = null;
        var groupText = new System.Text.StringBuilder();

        for (var r = 0; r < s.Rows; r++)
        {
            var a = s.Display(r, 0).Trim();
            var b = s.Display(r, 1).Trim();
            var g = FirstLong(s, r, 6, 7, 8);
            var k = FirstLong(s, r, 10, 11);
            var amount = FirstNumber(s, r, 14, 15, 17);
            var formula = FirstFormula(s, r, 14, 15, 17);

            if (IsSectionHeader(a, b))
            {
                FlushGroup(current, groupText, doc, est);
                section = string.IsNullOrWhiteSpace(a) ? b : a;
                current = null;
                continue;
            }

            if (IsTotalRow(b) || IsTotalRow(a))
            {
                FlushGroup(current, groupText, doc, est);
                if (b.Contains("Всего", StringComparison.OrdinalIgnoreCase) || a.Contains("Всего", StringComparison.OrdinalIgnoreCase))
                    est.ExcelAmount = amount;
                var vol = Regex.Match(k + " " + g, @"K\s*объем\s*=\s*([\d,.\s]+)", RegexOptions.IgnoreCase);
                if (vol.Success)
                    est.VolumeK = NumberParse.TryParse(vol.Groups[1].Value);
                current = null;
                continue;
            }

            if (IsAccrual(b))
            {
                FlushGroup(current, groupText, doc, est);
                var kv = Regex.Match(g, @"К\d?\s*=\s*([\d,.\s]+)");
                var val = kv.Success ? NumberParse.TryParse(kv.Groups[1].Value) : null;
                if (g.Contains("оптимизац", StringComparison.OrdinalIgnoreCase) && val is decimal opt)
                    est.OptimizationK = opt;
                if (g.Contains("районн", StringComparison.OrdinalIgnoreCase) && val is decimal dist)
                    est.DistrictK = dist;
                var acc = new Position
                {
                    Number = a,
                    Name = b,
                    Kind = PositionKind.Accrual,
                    Basis = new Basis { Raw = g },
                    Formula = k,
                    ExcelFormula = formula,
                    ExcelAmount = amount,
                    Origin = OriginKind.Excel,
                    SheetName = s.Name,
                    EstimateNumber = est.Number
                };
                if (val is decimal av)
                    FactorBinder.Upsert(acc, "K1", av, FactorBinder.GuessRole("K1", av, g), FactorSource.Text);
                est.Positions.Add(acc);
                current = null;
                continue;
            }

            if (int.TryParse(a, out _) && b.Length > 5 && !b.StartsWith("Коэффициент", StringComparison.Ordinal)
                && (amount is not null || formula is not null || g.Contains("НЗ", StringComparison.Ordinal)
                    || g.Contains("A =", StringComparison.Ordinal) || g.Contains("СБЦ", StringComparison.OrdinalIgnoreCase)))
            {
                FlushGroup(current, groupText, doc, est);
                current = Build707Position(a, b, g, k, amount, formula, s.Name, est.Number, section);
                est.Positions.Add(current);
                groupText.Clear();
                groupText.AppendLine(b).AppendLine(g).AppendLine(k);
                continue;
            }

            if (current is null) continue;
            groupText.AppendLine(b).AppendLine(g).AppendLine(k);
            ApplyCoeffLine(current, b, g);
        }

        FlushGroup(current, groupText, doc, est);
        return est;
    }

    internal static Estimate Parse2PSbc(SheetTable s, PirDocument doc)
    {
        var est = new Estimate
        {
            Form = EstimateForm.Form2PSbc,
            Sheet = s.Name,
            Factors = [DocumentFactory.DefaultKi1()],
            Title = FirstNonEmpty(s, 5, 2, 5, 1) ?? s.Name,
            Number = ExtractSmetaNumber(s) ?? GuessNumberFromSheet(s.Name)
        };
        est.Id = est.Number ?? s.Name;
        if (string.IsNullOrWhiteSpace(doc.Project.Designer))
            doc.Project.Designer = FirstNonEmpty(s, 7, 2) ?? doc.Project.Designer;
        if (string.IsNullOrWhiteSpace(doc.Project.Customer))
            doc.Project.Customer = FirstNonEmpty(s, 10, 2) ?? doc.Project.Customer;

        Position? current = null;
        string? section = null;
        var group = new System.Text.StringBuilder();
        for (var r = 0; r < s.Rows; r++)
        {
            var no = s.Display(r, 0).Trim();
            var name = s.Display(r, 1).Trim();
            var basis = s.Display(r, 2).Trim();
            var formulaText = s.Display(r, 3).Trim();
            var amount = s.Number(r, 4);
            var excelFormula = s.Formula(r, 4);

            if (no.Length == 0 && name.Length > 0 && amount is null && !name.StartsWith("К=", StringComparison.Ordinal)
                && !basis.StartsWith("К=", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("ИТОГО", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("Всего", StringComparison.OrdinalIgnoreCase))
            {
                FlushSbc(current, group, null, doc, est);
                section = name;
                current = null;
                continue;
            }

            if (name.StartsWith("ИТОГО", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Итого", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Всего", StringComparison.OrdinalIgnoreCase))
            {
                FlushSbc(current, group, null, doc, est);
                if (name.Contains("оптимизац", StringComparison.OrdinalIgnoreCase))
                    est.OptimizationK = NumberParse.TryParse(formulaText.Replace("К=", "", StringComparison.OrdinalIgnoreCase)) ?? 0.8m;
                else if (name.Contains("районн", StringComparison.OrdinalIgnoreCase))
                    est.DistrictK = NumberParse.TryParse(formulaText.Replace("К=", "", StringComparison.OrdinalIgnoreCase)) ?? 1.08m;
                else if (name.Contains("Всего", StringComparison.OrdinalIgnoreCase))
                {
                    est.ExcelAmount = amount;
                    var vol = Regex.Match(formulaText, @"K\s*объем\s*=\s*([\d,.\s]+)", RegexOptions.IgnoreCase);
                    if (vol.Success) est.VolumeK = NumberParse.TryParse(vol.Groups[1].Value);
                }
                current = null;
                continue;
            }

            if (int.TryParse(no, out _) && name.Length > 5)
            {
                FlushSbc(current, group, null, doc, est);
                current = ParseSbcFormula(no, name, basis, formulaText, amount, excelFormula, s.Name, est.Number, section);
                est.Positions.Add(current);
                group.Clear();
                group.AppendLine(basis).AppendLine(formulaText);
                continue;
            }

            if (current is not null)
            {
                group.AppendLine(basis).AppendLine(name);
                if (basis.StartsWith("К=", StringComparison.OrdinalIgnoreCase)
                    || basis.StartsWith("К1=", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(basis, @"^К\d?\s*="))
                    ApplySbcCoeff(current, basis);
            }
        }

        FlushSbc(current, group, null, doc, est);
        return est;
    }

    private static void FlushGroup(Position? current, System.Text.StringBuilder group, PirDocument doc, Estimate est)
    {
        if (current is null || current.Kind != PositionKind.Work) { group.Clear(); return; }
        BindFormulaAndText(current, group.ToString(), doc, est);
        group.Clear();
    }

    private static void FlushSbc(Position? current, System.Text.StringBuilder group, string? _, PirDocument doc, Estimate est)
    {
        if (current is null) { group.Clear(); return; }
        BindFormulaAndText(current, group.ToString(), doc, est);
        group.Clear();
    }

    private static void BindFormulaAndText(Position pos, string groupText, PirDocument doc, Estimate est)
    {
        var text = (pos.Basis?.Raw ?? "") + "\n" + (pos.Formula ?? "") + "\n" + groupText;
        var parsed = ExcelRoundFormula.Parse(pos.ExcelFormula)
                     ?? ExcelRoundFormula.Parse(pos.Formula);

        if (parsed is not null)
        {
            CompareAndSet(pos, "priceA", pos.PriceA, parsed.PriceA, text, @"A\s*=\s*", doc, est, thousands: true);
            if (parsed.PriceA is decimal fa) pos.PriceA = fa;
            if (parsed.PriceB is decimal fb) pos.PriceB = fb;
            if (parsed.Quantity is decimal fx) pos.Quantity = fx;
            if (parsed.Xmin is decimal xmin) pos.Xmin = xmin;
            if (parsed.Qty is decimal q && q > 0) pos.Qty = q;
            if (parsed.Evaluated is decimal ev && pos.ExcelAmount is null)
                pos.ExcelAmount = ev;

            var named = ExtractNamedFactors(text);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skipQty = parsed.Qty is not null;
            var i = 0;
            foreach (var m in parsed.Multipliers)
            {
                i++;
                if (skipQty && i == 1 && parsed.Qty is decimal pq && Almost(m, pq))
                    continue;

                var hit = named.FirstOrDefault(n => !used.Contains(n.Id) && Almost(n.Value, m));
                if (hit is not null)
                {
                    used.Add(hit.Id);
                    FactorBinder.Upsert(pos, hit.Id, m, hit.Role, FactorSource.Both);
                    var f = pos.Factors.First(x => x.Identifier == hit.Id);
                    f.LetterRef = hit.Letter;
                    f.Caption = hit.Caption ?? hit.Id;
                }
                else
                {
                    var id = GuessMultiplierId(pos, m, text);
                    FactorBinder.Upsert(pos, id, m, FactorBinder.GuessRole(id, m, text), FactorSource.Formula);
                }
            }

            foreach (var n in named)
            {
                if (used.Contains(n.Id)) continue;
                if (n.Id is "Количество" or "Кол") continue;
                FactorBinder.Upsert(pos, n.Id, n.Value, n.Role, FactorSource.Text);
                doc.Issues.Add(new Clarification
                {
                    Severity = ClarificationSeverity.Info,
                    Code = "coeff-unmatched",
                    Message = $"«{n.Id}={n.Value}» есть в тексте группы, но нет такого множителя в формуле Excel",
                    EstimateId = est.Id,
                    PositionId = pos.Id,
                    Field = "factors",
                    SuggestedAction = "Проверить, должен ли коэффициент входить в ROUND"
                });
            }
        }
        else
        {
            ApplyTextOnly(pos, text);
        }

        if (pos.Basis is { } basis)
            pos.CatalogCode ??= AprXml.RateCode(basis);

        if (pos.Xmin is decimal xmin2 && pos.Xmax is decimal xmax && pos.Items.Count == 0)
        {
            pos.Items.Add(new IntervalItem
            {
                Caption = $"{xmin2}–{xmax}",
                MinQuantity = xmin2,
                MaxQuantity = xmax,
                PriceA = pos.PriceA,
                PriceB = pos.PriceB
            });
        }
    }

    private static void CompareAndSet(Position pos, string field, decimal? textVal, decimal? formulaVal,
        string text, string prefix, PirDocument doc, Estimate est, bool thousands)
    {
        var fromText = thousands ? ExtractRubParam(text, prefix) : ExtractPlain(text, prefix);
        if (formulaVal is decimal fv && fromText is decimal tv && Math.Abs(fv - tv) > 1m)
        {
            doc.Issues.Add(new Clarification
            {
                Severity = ClarificationSeverity.Warning,
                Code = "formula-text-mismatch",
                Message = $"{field}: формула Excel {fv} ≠ текст {tv}. Взято значение формулы.",
                EstimateId = est.Id,
                PositionId = pos.Id,
                Field = field,
                SuggestedAction = "Сверить обоснование и ячейку ROUND"
            });
        }
    }

    private static void ApplyTextOnly(Position pos, string text)
    {
        pos.PriceA ??= ExtractRubParam(text, @"A\s*=\s*");
        pos.PriceB ??= ExtractRubParam(text, @"B\s*=\s*");
        pos.Quantity ??= ExtractPlain(text, @"(?:X(?:зад)?|Хзад|L)\s*=\s*")
                         ?? ExtractPlain(text, @"Протяженность дороги L\s*=\s*");
        pos.Xmin ??= ExtractPlain(text, @"X(?:мин|min)\s*=\s*");
        pos.Xmax ??= ExtractPlain(text, @"X(?:макс|max)\s*=\s*");
        var qty = ExtractPlain(text, @"Количество\s*=\s*");
        if (qty is decimal q) pos.Qty = q;
        foreach (var n in ExtractNamedFactors(text))
        {
            if (n.Id is "Количество" or "Кол") continue;
            FactorBinder.Upsert(pos, n.Id, n.Value, n.Role, FactorSource.Text);
        }
    }

    private static Position Build707Position(string no, string name, string basis, string formula, decimal? amount,
        string? excelFormula, string sheet, string? estNo, string? section)
    {
        var pos = new Position
        {
            Number = no,
            Name = name,
            Kind = PositionKind.Work,
            Basis = BasisParser.Parse(basis),
            Formula = formula,
            ExcelFormula = excelFormula,
            ExcelAmount = amount,
            Origin = OriginKind.Excel,
            SheetName = sheet,
            EstimateNumber = estNo,
            Section = section,
            Qty = 1m
        };

        var text = basis + "\n" + formula;
        pos.PriceA = ExtractRubParam(text, @"A\s*=\s*");
        pos.PriceB = ExtractRubParam(text, @"B\s*=\s*");
        pos.Quantity = ExtractPlain(text, @"(?:X(?:зад)?|Хзад|L)\s*=\s*")
                       ?? ExtractPlain(text, @"Протяженность дороги L\s*=\s*");
        pos.Xmin = ExtractPlain(text, @"X(?:мин|min)\s*=\s*");
        pos.Xmax = ExtractPlain(text, @"X(?:макс|max)\s*=\s*");
        var qty = ExtractPlain(text, @"Количество\s*=\s*");
        if (qty is decimal q) pos.Qty = q;
        var unit = Regex.Match(text, @"(?:Xзад|Хзад|L)\s*=\s*[\d\s.,]+\s*\(([^)]+)\)");
        if (unit.Success) pos.QuantityUnit = unit.Groups[1].Value.Trim();
        pos.CatalogCode = AprXml.RateCode(pos.Basis);
        return pos;
    }

    private static void ApplyCoeffLine(Position pos, string b, string g)
    {
        foreach (var n in ExtractNamedFactors(b + "\n" + g))
        {
            if (n.Id is "Количество" or "Кол") continue;
            FactorBinder.Upsert(pos, n.Id, n.Value, n.Role, FactorSource.Text);
            var f = pos.Factors.FirstOrDefault(x => x.Identifier == n.Id);
            if (f is not null && n.Letter is not null) f.LetterRef = n.Letter;
        }
        if (b.Contains("инд.", StringComparison.OrdinalIgnoreCase))
        {
            var f = pos.Factors.FirstOrDefault(x => x.Identifier == "Ктек");
            if (f is not null) f.Caption = b.Trim();
        }
    }

    private static Position ParseSbcFormula(string no, string name, string basis, string formula, decimal? amount,
        string? excelFormula, string sheet, string? estNo, string? section)
    {
        var sbcBasis = ParseSbcBasis(basis);
        var pos = new Position
        {
            Number = no,
            Name = name,
            Kind = PositionKind.Work,
            Basis = sbcBasis,
            Formula = formula,
            ExcelFormula = excelFormula,
            ExcelAmount = amount,
            Origin = OriginKind.Excel,
            SheetName = sheet,
            EstimateNumber = estNo,
            Section = section,
            Qty = 1m,
            CatalogCode = AprXml.RateCode(sbcBasis)
        };

        var src = excelFormula ?? formula;
        var parsed = ExcelRoundFormula.Parse(src);
        if (parsed is not null)
        {
            pos.PriceA = parsed.PriceA;
            pos.PriceB = parsed.PriceB;
            pos.Quantity = parsed.Quantity;
            if (parsed.Qty is decimal q && parsed.PriceB is not 0) pos.Qty = q;
        }
        else
        {
            var norm = formula.Replace("х", "x", StringComparison.OrdinalIgnoreCase).Replace("×", "x");
            var abx = Regex.Match(norm, @"\(([\d\s.,]+)\s*\+\s*([\d\s.,]+)\s*x\s*([\d\s.,]+)\)", RegexOptions.IgnoreCase);
            if (abx.Success)
            {
                pos.PriceA = NumberParse.TryParse(abx.Groups[1].Value);
                pos.PriceB = NumberParse.TryParse(abx.Groups[2].Value);
                pos.Quantity = NumberParse.TryParse(abx.Groups[3].Value);
            }
        }

        return pos;
    }

    private static void ApplySbcCoeff(Position pos, string basis)
    {
        var m = Regex.Match(basis, @"К(\d?)\s*=\s*([\d,.\s]+)");
        if (!m.Success) return;
        var id = string.IsNullOrEmpty(m.Groups[1].Value) ? GuessSbcId(basis) : "K" + m.Groups[1].Value;
        var v = NumberParse.TryParse(m.Groups[2].Value);
        if (v is decimal d)
            FactorBinder.Upsert(pos, id, d, FactorBinder.GuessRole(id, d, basis), FactorSource.Text);
    }

    private static string GuessSbcId(string basis)
    {
        if (basis.Contains("стади", StringComparison.OrdinalIgnoreCase)) return "Кст";
        if (basis.Contains("инфляц", StringComparison.OrdinalIgnoreCase)) return "Kинфл";
        return "K";
    }

    private static Basis ParseSbcBasis(string raw)
    {
        var b = BasisParser.Parse(raw);
        if (b.Kind == "Unknown" && !string.IsNullOrWhiteSpace(raw))
            b.Kind = "SBC";
        return b;
    }

    private sealed record NamedFactor(string Id, decimal Value, string Role, string? Caption, string? Letter);

    private static List<NamedFactor> ExtractNamedFactors(string text)
    {
        var list = new List<NamedFactor>();
        var letter = ExtractLetter(text);
        void Add(string id, decimal v, string role, string? cap = null) =>
            list.Add(new NamedFactor(id, v, role, cap, letter));

        foreach (Match m in Regex.Matches(text, @"(Кст|Ктек|К'?пон|K1|Ки1|Количество)\s*=\s*([\d\s]+(?:[.,]\d+)?)", RegexOptions.IgnoreCase))
        {
            var id = m.Groups[1].Value.Replace("'", "");
            var v = NumberParse.TryParse(m.Groups[2].Value);
            if (v is not decimal d) continue;
            var role = id.Contains("тек", StringComparison.OrdinalIgnoreCase) ? "index"
                : id.Contains("ст", StringComparison.OrdinalIgnoreCase) || id.Contains("Ки1", StringComparison.OrdinalIgnoreCase) ? "stage"
                : id.Contains("пон", StringComparison.OrdinalIgnoreCase) ? "kpon"
                : id.Contains("Количество", StringComparison.OrdinalIgnoreCase) ? "qty"
                : "other";
            Add(id == "Количество" ? "Количество" : id, d, role);
        }

        foreach (Match m in Regex.Matches(text, @"К(\d?)\s*=\s*([\d,.\s]+)([^\n]*)"))
        {
            if (m.Value.Contains("Кст", StringComparison.OrdinalIgnoreCase) || m.Value.Contains("Ктек", StringComparison.OrdinalIgnoreCase))
                continue;
            var v = NumberParse.TryParse(m.Groups[2].Value);
            if (v is not decimal d) continue;
            var rest = m.Groups[3].Value;
            var id = string.IsNullOrEmpty(m.Groups[1].Value) ? GuessSbcId("К=" + rest) : "K" + m.Groups[1].Value;
            if (list.Any(x => Almost(x.Value, d) && x.Id == id)) continue;
            Add(id, d, FactorBinder.GuessRole(id, d, rest), rest.Trim());
        }

        return list;
    }

    private static string? ExtractLetter(string text)
    {
        var m = Regex.Match(text, @"Письмо[^\n]{10,180}");
        return m.Success ? m.Value.Trim() : null;
    }

    private static string GuessMultiplierId(Position pos, decimal m, string text)
    {
        if (m is 0.5m or 0.52m or 0.58m or 0.6m or 0.4m && pos.Factors.All(f => f.Role != "stage"))
            return "Кст";
        if (m is >= 1.2m and <= 2.5m && pos.Factors.All(f => f.Role != "index")
            && (text.Contains("инд.", StringComparison.OrdinalIgnoreCase) || text.Contains("Ктек", StringComparison.OrdinalIgnoreCase)))
            return "Ктек";
        if (Almost(m, 0.8m) && text.Contains("Газпром", StringComparison.OrdinalIgnoreCase))
            return "Kгазпром";
        if (Almost(m, 1.08m))
            return "Kрайон";
        var n = pos.Factors.Count(f => f.Identifier.StartsWith('K') || f.Identifier.StartsWith('К')) + 1;
        return "K" + n;
    }

    private static bool Almost(decimal a, decimal b) => Math.Abs(a - b) < 0.0005m;

    private static decimal? ExtractRubParam(string text, string prefix)
    {
        var m = Regex.Match(text, prefix + @"([\d\s]+(?:[.,]\d+)?)\s*(тыс)?", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var v = NumberParse.TryParse(m.Groups[1].Value);
        if (v is null) return null;
        var thous = m.Groups[2].Success || text.Contains("тыс", StringComparison.OrdinalIgnoreCase);
        if (thous && v < 100_000) return Math.Round(v.Value * 1000m, 3);
        return v;
    }

    private static decimal? ExtractPlain(string text, string prefix)
    {
        var m = Regex.Match(text, prefix + @"([\d\s]+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
        return m.Success ? NumberParse.TryParse(m.Groups[1].Value) : null;
    }

    private static void ApplyProjectIndexFromPositions(PirDocument doc)
    {
        static bool IsKtekName(Factor f) =>
            f.Identifier.Equals("Ктек", StringComparison.OrdinalIgnoreCase)
            || f.Identifier.Equals("Ktek", StringComparison.OrdinalIgnoreCase);

        static bool IsPirIndexValue(decimal v) => v is >= 1.0m and <= 3.0m;

        static List<decimal> Collect(IEnumerable<Estimate> estimates, Func<Factor, bool> pred) =>
            estimates
                .SelectMany(e => e.Positions)
                .SelectMany(p => p.Factors)
                .Where(pred)
                .Select(f => f.WorkDoc ?? f.ProjectDoc)
                .Where(v => v is decimal d && IsPirIndexValue(d))
                .Select(v => v!.Value)
                .ToList();

        var ktekVals = Collect(doc.Estimates.Where(e => e.Form == EstimateForm.Form2P707), IsKtekName);
        if (ktekVals.Count == 0)
            ktekVals = Collect(doc.Estimates, IsKtekName);
        if (ktekVals.Count == 0)
            ktekVals = Collect(doc.Estimates.Where(e => e.Form == EstimateForm.Form2P707), f => f.Role == "index");
        if (ktekVals.Count == 0) return;

        var mode = ktekVals.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        doc.Project.CurrentIndex = mode;
        var sample = doc.Estimates.SelectMany(e => e.Positions).SelectMany(p => p.Factors)
            .FirstOrDefault(f => IsKtekName(f) && (f.WorkDoc ?? f.ProjectDoc) == mode)
            ?? doc.Estimates.SelectMany(e => e.Positions).SelectMany(p => p.Factors)
                .FirstOrDefault(f => f.Role == "index" && (f.WorkDoc ?? f.ProjectDoc) == mode);
        doc.Project.CurrentIndexLetter = sample?.LetterRef;
        if (sample is not null && sample.Caption != "Ктек" && !string.IsNullOrWhiteSpace(sample.Caption))
            doc.Project.CurrentIndexCaption = sample.Caption;
    }

    private static bool IsSectionHeader(string a, string b)
    {
        if (int.TryParse(a, out _)) return false;
        if (a.Length > 8 && !a.StartsWith("Смета", StringComparison.Ordinal)) return true;
        if (a.Length == 0 && b.Length > 0 && !b.StartsWith("Коэффициент", StringComparison.Ordinal)
            && !b.StartsWith("Стадия", StringComparison.Ordinal)
            && !b.StartsWith("инд.", StringComparison.Ordinal)
            && !b.StartsWith("Новое", StringComparison.Ordinal)
            && !b.StartsWith("Итого", StringComparison.Ordinal)
            && !b.StartsWith("Всего", StringComparison.Ordinal))
            return b.Length > 20 || b.Contains("Этап", StringComparison.Ordinal);
        return false;
    }

    private static bool IsTotalRow(string t) =>
        t.Contains("Итого по смете", StringComparison.OrdinalIgnoreCase)
        || t.Contains("Всего по смете", StringComparison.OrdinalIgnoreCase);

    private static bool IsAccrual(string t) =>
        t.Contains("Новое начисление", StringComparison.OrdinalIgnoreCase);

    internal static string? ExtractSmetaNumber(SheetTable s)
    {
        for (var r = 0; r < Math.Min(8, s.Rows); r++)
        for (var c = 0; c < Math.Min(8, s.Cols); c++)
        {
            var m = Regex.Match(s.Display(r, c), @"Смета\s*№\s*([\d.]+)");
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    internal static string? GuessNumberFromSheet(string name)
    {
        var m = Regex.Match(name, @"(\d+\.\d+(?:\.\d+)?)");
        return m.Success ? m.Groups[1].Value : null;
    }

    internal static string? FirstNonEmpty(SheetTable s, params int[] rc)
    {
        for (var i = 0; i + 1 < rc.Length; i += 2)
        {
            var t = s.Display(rc[i], rc[i + 1]).Trim();
            if (t.Length > 0 && !t.StartsWith("Наименование", StringComparison.Ordinal))
                return t;
        }
        return null;
    }

    private static string FirstLong(SheetTable s, int row, params int[] cols)
    {
        foreach (var c in cols)
        {
            var t = s.Display(row, c).Trim();
            if (t.Length > 0) return t;
        }
        return "";
    }

    internal static decimal? FirstNumber(SheetTable s, int row, params int[] cols)
    {
        foreach (var c in cols)
        {
            var n = s.Number(row, c);
            if (n is not null) return n;
        }
        return null;
    }

    internal static string? FirstFormula(SheetTable s, int row, params int[] cols)
    {
        foreach (var c in cols)
        {
            var f = s.Formula(row, c);
            if (!string.IsNullOrWhiteSpace(f)) return f;
        }
        return null;
    }
}
