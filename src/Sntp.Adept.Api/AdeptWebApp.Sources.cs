using System.Diagnostics;
using Sntp.Adept.Domain.Documents;

namespace Sntp.Adept.Api;

public static partial class AdeptWebApp
{
    private static void MapSources(WebApplication app)
    {
        app.MapGet("/api/projects/{id}/sources", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            return Results.Ok(new { items = SourceCatalog.List(doc), folder = SourceCatalog.Ensure(doc) });
        });

        app.MapPost("/api/projects/{id}/sources", async (string id, HttpRequest http, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            if (string.IsNullOrWhiteSpace(doc.Project.WorkFolder))
                return Results.BadRequest(new { error = "У объекта нет рабочей папки" });
            if (http.Form.Files.Count == 0) return Results.BadRequest(new { error = "Нет файла" });
            foreach (var file in http.Form.Files)
            {
                await using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                SourceCatalog.Add(doc, file.FileName, ms.ToArray());
            }
            return Results.Ok(new { items = SourceCatalog.List(doc) });
        }).DisableAntiforgery();

        app.MapPost("/api/projects/{id}/sources/reveal", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var folder = SourceCatalog.Ensure(doc);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return Results.Ok(new { ok = true, folder });
        });

        app.MapPost("/api/projects/{id}/sources/open", async (string id, SourceNameBody body, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            string path;
            try { path = SourceCatalog.PathOf(doc, body.Name); }
            catch (FileNotFoundException) { return Results.NotFound(new { error = "Файл не найден" }); }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Results.Ok(new { ok = true, path });
        });

        app.MapPost("/api/projects/{id}/sources/remove", async (string id, SourceNameBody body, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            SourceCatalog.Remove(doc, body.Name);
            return Results.Ok(new { items = SourceCatalog.List(doc) });
        });
    }
}

public sealed class SourceNameBody
{
    public string Name { get; set; } = "";
}
