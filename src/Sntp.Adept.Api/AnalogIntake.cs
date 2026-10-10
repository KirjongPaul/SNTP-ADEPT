using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;
using Sntp.Adept.Domain.Paste;
using Sntp.Adept.Excel;
using Sntp.Adept.Export;

namespace Sntp.Adept.Api;

public static class AnalogIntake
{
    public static AnalogCard ImportNew(PirDocument doc, string fileName, byte[] bytes, IReadOnlyList<ReferenceBook> books)
    {
        _ = books;
        var card = AnalogCatalog.AddSource(doc, fileName, bytes);
        RememberSheets(card, bytes);
        card.Status = "stored";
        AnalogCatalog.Touch(doc, card);
        return card;
    }

    public static List<Clarification> Reimport(PirDocument doc, AnalogCard card, IReadOnlyList<ReferenceBook> books)
    {
        var path = AnalogCatalog.SourcePath(doc, card);
        var bytes = File.ReadAllBytes(path);
        AnalogCatalog.ReplaceSource(doc, card, bytes);
        return Recognize(doc, card, bytes, books);
    }

    public static (List<AnalogRowScore> Scores, bool NeedsTune, List<Position> Positions) RecognizePaste(
        PirDocument doc, string? text, string? html, IReadOnlyList<ReferenceBook> books)
    {
        var preview = ClipboardGridParser.Parse(text, html);
        var positions = ClipboardGridParser.ToPositions(preview);
        AnalogMatcher.Apply(positions, AnalogCatalog.LoadMaps(doc));
        var holder = new Estimate { Id = "paste", Positions = positions };
        var scores = AnalogMatcher.Score([holder], books);
        return (scores, AnalogMatcher.NeedsTune(scores), positions);
    }

    private static List<Clarification> Recognize(PirDocument doc, AnalogCard card, byte[] bytes, IReadOnlyList<ReferenceBook> books)
    {
        AnalogRead? read = null;
        try
        {
            if (card.Format == "unknown")
            {
                card.Status = "unknown";
                AnalogCatalog.Touch(doc, card);
                return [];
            }
            RememberSheets(card, bytes);
            var parsed = card.Format == "grand" ? ReadGrand(card, bytes) : ExcelImporter.Import(new MemoryStream(bytes), card.SourceFile);
            if (card.Format == "grand")
                AnalogOutline.Fill(card, parsed.Estimates, parsed.Project.StageLabel, parsed.Project.RegionName, parsed.Grand?.ProgramVersion);
            var maps = AnalogCatalog.LoadMaps(doc);
            foreach (var est in parsed.Estimates)
                AnalogMatcher.Apply(est.Positions, maps);
            read = new AnalogRead
            {
                Estimates = parsed.Estimates,
                Scores = AnalogMatcher.Score(parsed.Estimates, books)
            };
            AnalogCatalog.WriteRead(doc, card.Id, read);
            var issues = Clarifications(card, read, maps);
            card.Status = issues.Any(i => i.Severity == ClarificationSeverity.Error) ? "recognized" : "imported";
            if (!card.NameEdited)
            {
                var title = parsed.Estimates.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Title))?.Title;
                if (!string.IsNullOrWhiteSpace(title)) card.SystemName = title.Trim();
            }
            AnalogCatalog.Touch(doc, card);
            return issues;
        }
        catch (Exception ex)
        {
            card.Status = "failed";
            card.SystemName = string.IsNullOrWhiteSpace(card.SystemName) ? card.SourceName : card.SystemName;
            AnalogCatalog.Touch(doc, card);
            return
            [
                new Clarification
                {
                    Severity = ClarificationSeverity.Error,
                    Code = "analog-match",
                    Field = "analog:" + card.Id,
                    Message = $"{card.SystemName}: файл не разобран ({ex.Message})",
                    SuggestedAction = "Проверьте, что это книга Excel или смета Grand, и повторите распознавание"
                }
            ];
        }
    }

    private static void RememberSheets(AnalogCard card, byte[] bytes)
    {
        if (card.Format != "excel") return;
        try { card.Sheets = SheetTable.LoadWorkbook(new MemoryStream(bytes)).Select(s => s.Name).ToList(); }
        catch { card.Sheets = []; }
    }

    private static List<Clarification> Clarifications(AnalogCard card, AnalogRead read, AnalogMappings maps)
    {
        var field = "analog:" + card.Id;
        var list = new List<Clarification>();
        foreach (var score in read.Scores.Where(s => s.LooksLikeBook && s.Confidence < 0.9))
        {
            list.Add(new Clarification
            {
                Severity = score.Confidence < 0.6 ? ClarificationSeverity.Error : ClarificationSeverity.Warning,
                Code = "analog-match",
                Field = field,
                EstimateId = score.EstimateId,
                PositionId = score.PositionId,
                Message = $"{card.SystemName}: {score.Note}",
                SuggestedAction = score.Confidence < 0.6
                    ? "Уточните пункт сборника или правило коэффициента и повторите распознавание"
                    : "Сверьте полосу и значения A/B, затем повторите распознавание"
            });
        }
        var known = new HashSet<string>(maps.Rules.Select(r => r.From), StringComparer.OrdinalIgnoreCase);
        foreach (var pos in read.Estimates.SelectMany(e => e.Positions))
        foreach (var factor in pos.Factors)
        {
            var text = factor.Identifier + " " + factor.Caption;
            if (!text.Contains("кст", StringComparison.OrdinalIgnoreCase) && !text.Contains("кст", StringComparison.OrdinalIgnoreCase))
                continue;
            if (text.Contains("стесн", StringComparison.OrdinalIgnoreCase) || known.Contains(factor.Identifier) || known.Contains(factor.Caption))
                continue;
            list.Add(new Clarification
            {
                Severity = ClarificationSeverity.Warning,
                Code = "analog-match",
                Field = field,
                EstimateId = pos.EstimateNumber,
                PositionId = pos.Id,
                Message = $"{card.SystemName}: коэффициент «{factor.Caption}» не различен — стеснённость или стадийность",
                SuggestedAction = "Добавьте правило в настройках файла и повторите распознавание"
            });
        }
        return list;
    }

    private static PirDocument ReadGrand(AnalogCard card, byte[] bytes)
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + Path.GetExtension(card.SourceFile));
        File.WriteAllBytes(tmp, bytes);
        try { return GrandXml.LoadFile(tmp); }
        finally { try { File.Delete(tmp); } catch { /* ignore */ } }
    }
}
