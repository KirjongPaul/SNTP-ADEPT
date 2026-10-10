using System.Diagnostics;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Models;
using Sntp.Adept.Excel;

namespace Sntp.Adept.Api;

public static partial class AdeptWebApp
{
    private static void MapAnalogs(WebApplication app)
    {
        app.MapGet("/api/projects/{id}/analogs", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            var catalog = AnalogCatalog.Load(doc);
            var changed = false;
            foreach (var card in catalog.Items)
            {
                if (card.Format != "excel" || card.Sheets.Count > 0) continue;
                var path = AnalogCatalog.SourcePath(doc, card);
                if (!File.Exists(path)) continue;
                try
                {
                    card.Sheets = SheetTable.LoadWorkbook(path).Select(s => s.Name).ToList();
                    changed = true;
                }
                catch
                {
                    /* книга открыта в Excel */
                }
            }
            foreach (var card in catalog.Items)
            {
                if (card.Format != "grand" || card.Facts.Count > 0) continue;
                var read = AnalogCatalog.Read(doc, card.Id);
                if (read is null) continue;
                AnalogOutline.Fill(card, read.Estimates, null, null, null);
                changed = true;
            }
            if (changed) AnalogCatalog.Save(doc, catalog);
            return Results.Ok(catalog);
        });

        app.MapGet("/api/projects/{id}/analogs/{analogId}", async (string id, string analogId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var card = AnalogCatalog.Find(doc, analogId);
            if (card is null) return Results.NotFound(new { error = "Аналог не найден" });
            return Results.Ok(new { card, read = AnalogCatalog.Read(doc, analogId), maps = AnalogCatalog.LoadMaps(doc) });
        });

        app.MapPost("/api/projects/{id}/analogs/pick", async (string id, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            var folder = SourceCatalog.Ensure(doc);
            string[] paths;
            try
            {
                paths = LocalFileDialog.Pick(folder, "Исходные данные",
                [
                    ("Excel и Grand", "*.xlsx;*.xlsm;*.xls;*.xml;*.gsfx"),
                    ("Все файлы", "*.*")
                ]);
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = ex.Message, folder });
            }
            foreach (var path in paths)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                AnalogIntake.ImportNew(doc, Path.GetFileName(path), bytes, books.All());
            }
            return Results.Ok(new { items = AnalogCatalog.Load(doc).Items, folder, picked = paths.Length });
        });

        app.MapPost("/api/projects/{id}/analogs", async (string id, HttpRequest http, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            var file = http.Form.Files.GetFile("file") ?? http.Form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "Нет файла" });
            await using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var card = AnalogIntake.ImportNew(doc, file.FileName, ms.ToArray(), books.All());
            return Results.Ok(new { card, catalog = AnalogCatalog.Load(doc) });
        }).DisableAntiforgery();

        app.MapDelete("/api/projects/{id}/analogs/{analogId}", async (string id, string analogId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            if (AnalogCatalog.Find(doc, analogId) is null) return Results.NotFound(new { error = "Аналог не найден" });
            AnalogCatalog.Remove(doc, analogId);
            return Results.Ok(AnalogCatalog.Load(doc));
        });

        app.MapPost("/api/projects/{id}/analogs/{analogId}/reimport", async (string id, string analogId, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var card = AnalogCatalog.Find(doc, analogId);
            if (card is null) return Results.NotFound(new { error = "Аналог не найден" });
            var issues = AnalogIntake.Reimport(doc, card, books.All());
            var field = "analog:" + analogId;
            doc.Issues.RemoveAll(i => i.Code == "analog-match" && i.Field == field);
            doc.Issues.AddRange(issues);
            await store.SaveAsync(doc);
            return Results.Ok(new { items = AnalogCatalog.Load(doc).Items, document = doc });
        });

        app.MapPost("/api/projects/{id}/analogs/{analogId}/accept-estimates", async (string id, string analogId, AnalogAcceptEstimates body, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var read = AnalogCatalog.Read(doc, analogId);
            if (read is null) return Results.BadRequest(new { error = "Аналог ещё не распознан" });
            var chosen = body.EstimateIds is { Count: > 0 }
                ? read.Estimates.Where(e => body.EstimateIds.Contains(e.Id)).ToList()
                : read.Estimates;
            if (chosen.Count == 0) return Results.BadRequest(new { error = "Нет смет для приёма" });
            foreach (var est in chosen)
                WorkFolder.ImportEstimate(doc, est);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/analogs/{analogId}/accept-positions", async (string id, string analogId, AnalogAcceptPositions body, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, body.TargetEstimateId);
            if (est is null) return Results.NotFound(new { error = "Смета не найдена" });
            var read = AnalogCatalog.Read(doc, analogId);
            if (read is null) return Results.BadRequest(new { error = "Аналог ещё не распознан" });
            var positions = read.Estimates.SelectMany(e => e.Positions)
                .Where(p => body.PositionIds.Contains(p.Id) && p.Kind is PositionKind.Work or PositionKind.Text)
                .ToList();
            if (positions.Count == 0) return Results.BadRequest(new { error = "Нет расценок для приёма" });
            foreach (var pos in positions)
                WorkFolder.ImportPosition(est, pos);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPatch("/api/projects/{id}/analogs/{analogId}", async (string id, string analogId, AnalogRename body, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var catalog = AnalogCatalog.Load(doc);
            var card = catalog.Items.FirstOrDefault(c => string.Equals(c.Id, analogId, StringComparison.OrdinalIgnoreCase));
            if (card is null) return Results.NotFound(new { error = "Аналог не найден" });
            var name = (body.SystemName ?? "").Trim();
            if (name.Length == 0) return Results.BadRequest(new { error = "Пустое название" });
            card.SystemName = name;
            card.NameEdited = true;
            AnalogCatalog.Save(doc, catalog);
            return Results.Ok(AnalogCatalog.Load(doc));
        });

        app.MapPost("/api/projects/{id}/analogs/reveal", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var folder = AnalogCatalog.Root(doc);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return Results.Ok(new { ok = true, folder });
        });

        app.MapPost("/api/projects/{id}/analogs/{analogId}/open", async (string id, string analogId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var path = AnalogSource(doc, analogId);
            if (path is null) return Results.NotFound(new { error = "Исходник не найден" });
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Results.Ok(new { ok = true, path });
        });

        app.MapPost("/api/projects/{id}/analogs/{analogId}/reveal", async (string id, string analogId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var path = AnalogSource(doc, analogId);
            if (path is null) return Results.NotFound(new { error = "Исходник не найден" });
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + path + "\"",
                UseShellExecute = true
            });
            return Results.Ok(new { ok = true, path });
        });

        app.MapGet("/api/projects/{id}/analog-mappings", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            return Results.Ok(AnalogCatalog.LoadMaps(doc));
        });

        app.MapPut("/api/projects/{id}/analog-mappings", async (string id, AnalogMappings maps, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            AnalogCatalog.SaveMaps(doc, maps);
            return Results.Ok(maps);
        });

        app.MapPost("/api/projects/{id}/analog-paste", async (string id, PasteBody body, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var (scores, needs, positions) = AnalogIntake.RecognizePaste(doc, body.Text, body.Html, books.All());
            return Results.Ok(new
            {
                needsTune = needs,
                scores,
                rows = positions.Select(p => new { p.Id, p.Name, basis = p.Basis?.Raw, p.Kind })
            });
        });
    }

    private static string? AnalogSource(PirDocument doc, string analogId)
    {
        var card = AnalogCatalog.Find(doc, analogId);
        if (card is null) return null;
        var path = AnalogCatalog.SourcePath(doc, card);
        return File.Exists(path) ? path : null;
    }
}

public sealed class AnalogRename
{
    public string SystemName { get; set; } = "";
}

public sealed class AnalogAcceptEstimates
{
    public List<string>? EstimateIds { get; set; }
}

public sealed class AnalogAcceptPositions
{
    public string TargetEstimateId { get; set; } = "";
    public List<string> PositionIds { get; set; } = [];
}
