using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Catalog;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;
using Sntp.Adept.Domain.Paste;
using Sntp.Adept.Excel;

namespace Sntp.Adept.Api;

public static partial class AdeptWebApp
{
    private static void MapFolderAndCatalog(WebApplication app)
    {
        app.MapGet("/api/catalog/gazprom", async (CatalogStore catalog) =>
            Results.Ok(await catalog.VendorAsync()));

        app.MapGet("/api/catalog/groups", async (string? kind, CatalogStore catalog) =>
            Results.Ok(await catalog.GroupsAsync(kind)));

        app.MapPost("/api/catalog/search-line", async (SearchLineRequest req, CatalogStore catalog) =>
            Results.Ok(await catalog.SearchLineAsync(req.Text)));

        app.MapPost("/api/catalog/sync-fgis", async (FgisCsClient client, CatalogStore catalog) =>
        {
            var result = await catalog.SyncFgisAsync(client);
            return Results.Ok(result);
        });

        app.MapPost("/api/catalog/gazprom/import", async (HttpRequest http, CatalogStore catalog) =>
        {
            var file = http.Form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "Нет файла json/xlsx" });
            await using var stream = file.OpenReadStream();
            var rows = file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || file.FileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                ? GazpromSheet.Read(stream)
                : await System.Text.Json.JsonSerializer.DeserializeAsync<List<VendorCoeff>>(stream, Domain.Json.PirJson.Options) ?? [];
            var n = await catalog.ImportGazpromAsync(rows);
            return Results.Ok(new { imported = n });
        }).DisableAntiforgery();

        app.MapPost("/api/catalog/harvest", async (List<AdeptUiRate.HarvestRow> rows, CatalogStore catalog) =>
            Results.Ok(new { saved = await catalog.SaveHarvestAsync(rows) }));

        app.MapPost("/api/projects/open-folder", async (OpenFolderRequest req, ProjectStore store) =>
        {
            var doc = WorkFolder.Load(req.Path);
            try { await store.SaveAsync(doc); } catch { /* папка открывается и без SQL */ }
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/rescan", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            WorkFolder.Rescan(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/copy", async (string id, string estId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            var copy = WorkFolder.CopyEstimate(doc, estId);
            await store.SaveAsync(doc);
            return Results.Ok(new { document = doc, estimateId = copy.Id });
        });

        app.MapDelete("/api/projects/{id}/estimates/{estId}", async (string id, string estId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var heading = doc.Estimates.FirstOrDefault(e => e.Heading && (e.Id == estId || e.Number == estId));
            if (heading is not null)
                EstimateOutline.ReleaseChildren(doc, heading);
            if (!string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                WorkFolder.DeleteEstimate(doc, estId);
            else
                doc.Estimates.RemoveAll(e => e.Id == estId || e.Number == estId);
            EstimateOutline.Renumber(doc);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/paste", (string id, string estId, PasteBody body) =>
        {
            var preview = ClipboardGridParser.Parse(body.Text, body.Html);
            return Results.Ok(preview);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/paste/apply", async (string id, string estId, PasteBody body, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound();
            var preview = ClipboardGridParser.Parse(body.Text, body.Html);
            if (body.ColumnMap is { Count: > 0 }) preview.ColumnMap = body.ColumnMap;
            var positions = ClipboardGridParser.ToPositions(preview);
            AnalogMatcher.Apply(positions, AnalogCatalog.LoadMaps(doc));
            var scores = AnalogMatcher.Score([new Estimate { Positions = positions }], books.All());
            if (AnalogMatcher.NeedsTune(scores) && body.Force != true)
                return Results.BadRequest(new { error = "Нужно донастроить сопоставление", needsTune = true, scores });
            foreach (var pos in positions)
            {
                pos.EstimateNumber = est.Number;
                est.Positions.Add(pos);
            }
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/positions/{posId}/apply-rate", async (string id, string posId, LineHit hit, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var pos = doc.Estimates.SelectMany(e => e.Positions).FirstOrDefault(p => p.Id == posId);
            if (pos is null) return Results.NotFound();
            CatalogMatcher.ApplyHit(pos, hit);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapGet("/api/projects/{id}/drafts", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.Ok(new { scope = (ScopeDraft?)null, vor = Array.Empty<VorDraft>(), assignments = Array.Empty<string>() });
            return Results.Ok(new
            {
                scope = WorkFolder.ReadScopeDraft(doc),
                vor = WorkFolder.ReadVorDrafts(doc),
                assignments = WorkFolder.ListAssignments(doc)
            });
        });

        app.MapPost("/api/projects/{id}/scope/apply", async (string id, string? estimateId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "Нет рабочей папки: черновик состава туда некуда положить" });
            var draft = WorkFolder.ReadScopeDraft(doc);
            if (draft is null) return Results.BadRequest(new { error = "Нет черновика состава" });
            var est = FindEstimate(doc, estimateId ?? "") ?? doc.Estimates.FirstOrDefault();
            if (est is null) return Results.BadRequest(new { error = "Нет сметы" });
            foreach (var item in draft.Items)
            {
                est.Positions.Add(new Position
                {
                    Kind = PositionKind.Work,
                    Name = item.Name,
                    Section = item.Section,
                    Origin = OriginKind.Created,
                    EstimateNumber = est.Number
                });
            }
            draft.Status = "applied";
            WorkFolder.WriteScopeDraft(doc, draft);
            ApplyVor(doc, est);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/import-estimate", async (string id, ImportEstimateRequest req, ProjectStore store) =>
        {
            var target = await store.GetAsync(id);
            if (target is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(target.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            var origin = string.Equals(req.SourceProjectId, id, StringComparison.OrdinalIgnoreCase)
                ? target
                : await store.GetAsync(req.SourceProjectId);
            if (origin is null) return Results.NotFound();
            var src = FindEstimate(origin, req.SourceEstimateId);
            if (src is null) return Results.NotFound();
            WorkFolder.ImportEstimate(target, src);
            CalcEngine.Recalculate(target);
            await store.SaveAsync(target);
            return Results.Ok(target);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/import-position", async (string id, string estId, ImportPositionRequest req, ProjectStore store) =>
        {
            var target = await store.GetAsync(id);
            if (target is null) return Results.NotFound();
            var est = FindEstimate(target, estId);
            if (est is null) return Results.NotFound();
            var origin = string.Equals(req.SourceProjectId, id, StringComparison.OrdinalIgnoreCase)
                ? target
                : await store.GetAsync(req.SourceProjectId);
            if (origin is null) return Results.NotFound();
            var srcEst = FindEstimate(origin, req.SourceEstimateId);
            var pos = srcEst?.Positions.FirstOrDefault(p => p.Id == req.SourcePositionId);
            if (pos is null || pos.Kind is not (PositionKind.Work or PositionKind.Text))
                return Results.BadRequest(new { error = "Можно перенести строку расценки" });
            WorkFolder.ImportPosition(est, pos);
            CalcEngine.Recalculate(target);
            await store.SaveAsync(target);
            return Results.Ok(target);
        });

        app.MapGet("/api/projects/{id}/assignments/{name}", async (string id, string name, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            return Results.Ok(new { name, text = WorkFolder.ReadAssignmentText(doc, name) });
        });
    }

    private static void ApplyVor(PirDocument doc, Estimate est)
    {
        foreach (var vor in WorkFolder.ReadVorDrafts(doc))
        {
            foreach (var row in vor.Rows)
            {
                var pos = est.Positions.LastOrDefault(p => p.Name == row.Name) ?? est.Positions.LastOrDefault();
                if (pos is null || row.Quantity is null) continue;
                if (pos.Name == row.Name) pos.Quantity = row.Quantity;
            }
            vor.Status = "applied";
            WorkFolder.WriteVorDraft(doc, vor);
        }
    }
}

public sealed class SearchLineRequest
{
    public string? Text { get; set; }
}

public sealed class OpenFolderRequest
{
    public string Path { get; set; } = "";
}

public sealed class ImportEstimateRequest
{
    public string SourceProjectId { get; set; } = "";
    public string SourceEstimateId { get; set; } = "";
}

public sealed class ImportPositionRequest
{
    public string SourceProjectId { get; set; } = "";
    public string SourceEstimateId { get; set; } = "";
    public string SourcePositionId { get; set; } = "";
}

public sealed class PasteBody
{
    public string? Text { get; set; }
    public string? Html { get; set; }
    public List<string>? ColumnMap { get; set; }
    public bool? Force { get; set; }
}
