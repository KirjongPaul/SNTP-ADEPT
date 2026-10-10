using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Json;
using Sntp.Adept.Domain.Models;
using Sntp.Adept.Excel;
using Sntp.Adept.Export;

namespace Sntp.Adept.Api;

public static partial class AdeptWebApp
{
    public static WebApplication Build(string[] args, string contentRoot, int port)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot")
        });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddSingleton<ProjectStore>();
        builder.Services.AddSingleton<CatalogStore>();
        builder.Services.AddSingleton<BookStore>();
        builder.Services.AddSingleton<StaffDirectory>();
        builder.Services.AddSingleton<UserSettingsStore>();
        builder.Services.AddHttpClient<FgisCsClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        var app = builder.Build();
        try
        {
            _ = app.Services.GetRequiredService<ProjectStore>().WorkRoot;
            app.Services.GetRequiredService<ProjectStore>().InitializeAsync().GetAwaiter().GetResult();
            app.Services.GetRequiredService<CatalogStore>().InitializeAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Failed to initialize Adept* tables in SNTP_PPO.");
        }

        app.UseCors();

        var www = Path.Combine(contentRoot, "wwwroot");
        if (Directory.Exists(www))
        {
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(www)
            });
        }

        MapEndpoints(app);

        if (Directory.Exists(www))
        {
            app.MapFallback(async ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    ctx.Response.StatusCode = 404;
                    return;
                }
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.SendFileAsync(Path.Combine(www, "index.html"));
            });
        }

        return app;
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/health", () => Results.Ok(new { ok = true, name = "SNTP-ADEPT", time = DateTime.UtcNow }));

        app.MapGet("/api/projects", async (ProjectStore store) => Results.Ok(await store.ListAsync()));

        app.MapPost("/api/projects", async (CreateObjectRequest req, ProjectStore store) =>
        {
            var doc = DocumentFactory.CreateObject(req);
            store.EnsureFolder(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapGet("/api/projects/{id}", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            foreach (var est in doc.Estimates)
            {
                PositionOrder.Arrange(est);
                PositionOrder.FillTotals(est);
            }
            return Results.Ok(doc);
        });

        app.MapDelete("/api/projects/{id}", async (string id, ProjectStore store) =>
        {
            var deleted = await store.DeleteAsync(id);
            return deleted ? Results.Ok(new { ok = true, id }) : Results.NotFound();
        });

        app.MapPost("/api/projects/{id}/reload", async (string id, ProjectStore store) =>
        {
            PirDocument? doc;
            try
            {
                doc = await store.ReadFolderAsync(id);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            CalcEngine.Recalculate(doc);
            await store.WriteSqlAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/reveal", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            var folder = doc?.Project.WorkFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return Results.NotFound(new { error = "Папка объекта не найдена" });
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return Results.Ok(new { ok = true, folder });
        });

        app.MapPatch("/api/projects/{id}", async (string id, PatchProjectRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            if (req.ObjectName is not null) doc.Project.ObjectName = req.ObjectName;
            if (req.Stage is { } st)
            {
                doc.Project.Stage = st;
                doc.Project.StageLabel = StageHelper.ToLabel(st);
            }
            if (req.Designer is not null) doc.Project.Designer = req.Designer;
            if (req.Customer is not null) doc.Project.Customer = req.Customer;
            if (req.VatRate is { } vat) doc.Project.VatRate = vat;
            if (req.CurrentIndex is { } idx) doc.Project.CurrentIndex = idx;
            if (req.RoundMode is { } rm) doc.Project.RoundMode = rm;
            if (req.KponPolicy is { } kp) doc.Project.KponPolicy = kp;
            if (req.Phase is not null) doc.Project.Phase = req.Phase;
            if (req.Conditions is not null) doc.Project.Conditions = req.Conditions;
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates", async (string id, CreateLocalRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = DocumentFactory.CreateLocal(doc, req);
            await store.SaveAsync(doc);
            return Results.Ok(new { document = doc, estimate = est });
        });

        app.MapPost("/api/projects/{id}/estimates/form3p", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = DocumentFactory.CreateForm3P(doc);
            await store.SaveAsync(doc);
            return Results.Ok(new { document = doc, estimate = est });
        });

        app.MapPost("/api/projects/{id}/estimates/automation", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = DocumentFactory.CreateAutomation(doc);
            await store.SaveAsync(doc);
            return Results.Ok(new { document = doc, estimate = est });
        });

        app.MapPost("/api/projects/{id}/summary/headings", async (string id, HeadingPlace? body, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            try
            {
                Estimate est;
                if (body?.MemberIds is { Count: > 0 })
                {
                    est = EstimateOutline.Group(doc, body.MemberIds, body.Title);
                    DocumentFactory.RebuildSummary(doc);
                }
                else
                    est = DocumentFactory.CreateHeading(doc, body?.Title, body?.BeforeId);
                await store.SaveAsync(doc);
                return Results.Ok(new { document = doc, estimate = est });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/projects/{id}/summary/hide-stage", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            doc.Summary ??= new Summary1Ps();
            doc.Summary.HideStageHeading = true;
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/outline", async (string id, OutlineMoveRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            EstimateOutline.Move(doc, req.EstimateId, req.TargetId, req.AsChild);
            DocumentFactory.RebuildSummary(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/outline/level", async (string id, OutlineLevelRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            try
            {
                Estimate? created = null;
                if (req.Delta > 0)
                    created = EstimateOutline.Group(doc, req.EstimateIds, null);
                else if (req.Delta < 0)
                    EstimateOutline.Outdent(doc, req.EstimateIds);
                else
                    return Results.BadRequest(new { error = "Нет смены уровня." });
                DocumentFactory.RebuildSummary(doc);
                await store.SaveAsync(doc);
                return Results.Ok(new { document = doc, estimate = created });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPatch("/api/projects/{id}/estimates/{estId}", async (string id, string estId, PatchEstimateRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound();
            if (req.Title is not null) est.Title = req.Title;
            if (req.Labor is { } labor) est.Labor = labor;
            if (req.Asutp is { } asutp) est.Asutp = asutp;
            if (req.Conditions is not null) est.Conditions = req.Conditions;
            if (req.SuppressedConditionIds is not null) est.SuppressedConditionIds = req.SuppressedConditionIds;
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/positions", async (string id, string estId, SectionInsertRequest? req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound();
            var pos = string.IsNullOrWhiteSpace(req?.BeforeId)
                ? DocumentFactory.CreatePosition(est)
                : PositionOrder.InsertBlank(est, req.BeforeId);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(new { document = doc, position = pos });
        });

        app.MapDelete("/api/projects/{id}/estimates/{estId}/positions/{posId}", async (string id, string estId, string posId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null || !PositionOrder.RemoveRow(est, posId)) return Results.NotFound();
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/positions/move", async (string id, string estId, PositionMoveRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.BeforeId))
                PositionOrder.MoveLast(est, req.PositionId);
            else
                PositionOrder.MoveBefore(est, req.PositionId, req.BeforeId);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/sections", async (string id, string estId, SectionInsertRequest? req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound();
            var section = PositionOrder.InsertSection(est, req?.BeforeId);
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(new { document = doc, position = section });
        });

        app.MapDelete("/api/projects/{id}/estimates/{estId}/sections/{sectionId}", async (string id, string estId, string sectionId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound();
            if (!PositionOrder.RemoveSection(est, sectionId)) return Results.NotFound();
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/summary", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            DocumentFactory.RebuildSummary(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/recalculate", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapGet("/api/projects/{id}/clarifications", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            return doc is null ? Results.NotFound() : Results.Ok(doc.Issues);
        });

        app.MapPatch("/api/projects/{id}/positions/{posId}", async (string id, string posId, PatchPositionRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var pos = doc.Estimates.SelectMany(e => e.Positions).FirstOrDefault(p => p.Id == posId);
            if (pos is null) return Results.NotFound();
            if (req.Quantity is { } x) pos.Quantity = x;
            if (req.PriceA is { } a) pos.PriceA = a;
            if (req.PriceB is { } b) pos.PriceB = b;
            if (req.Name is not null) pos.Name = req.Name;
            if (req.Code is not null) pos.Code = req.Code;
            if (req.Qty is { } q) pos.Qty = q;
            if (req.Xmin is { } xmin) pos.Xmin = xmin;
            if (req.Xmax is { } xmax) pos.Xmax = xmax;
            if (req.Factors is { } factors) pos.Factors = factors;
            if (req.Items is { } items) pos.Items = items;
            if (req.RelativeFactors is { } rpd) pos.RelativeFactors = rpd;
            if (req.SuppressedConditionIds is not null) pos.SuppressedConditionIds = req.SuppressedConditionIds;
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/import/excel", async (HttpRequest http, ProjectStore store, CatalogStore catalog, ILoggerFactory log) =>
        {
            var file = http.Form.Files.GetFile("file") ?? http.Form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "Нет файла Excel" });

            await using var upload = new MemoryStream();
            await file.CopyToAsync(upload);
            var bytes = upload.ToArray();
            var fileName = file.FileName;

            var ctx = http.HttpContext;
            var jsonOptions = ctx.RequestServices
                .GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
            var response = ctx.Response;

            response.StatusCode = StatusCodes.Status200OK;
            response.Headers.ContentType = "text/event-stream; charset=utf-8";
            response.Headers.CacheControl = "no-cache, no-store";
            response.Headers.Pragma = "no-cache";
            ctx.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            async Task WriteEvent(string evt, object payload)
            {
                var json = JsonSerializer.Serialize(payload, jsonOptions);
                await response.WriteAsync($"event: {evt}\ndata: {json}\n\n");
                await response.Body.FlushAsync();
            }

            await WriteEvent("progress", new ImportProgress
            {
                Percent = 0,
                Phase = "open",
                Message = $"Файл получен · {fileName}"
            });

            var channel = Channel.CreateUnbounded<ImportProgress>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
            var importTask = Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes, writable: false);
                try
                {
                    return ExcelImporter.Import(ms, fileName, p => channel.Writer.TryWrite(p));
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            });

            try
            {
                await foreach (var p in channel.Reader.ReadAllAsync(ctx.RequestAborted))
                    await WriteEvent("progress", p);

                var doc = await importTask;
                store.EnsureFolder(doc);

                await WriteEvent("progress", new ImportProgress
                {
                    Percent = 90,
                    Phase = "persist",
                    Message = "Сохранение объекта в SNTP_PPO"
                });
                await store.SaveAsync(doc);

                try
                {
                    await WriteEvent("progress", new ImportProgress
                    {
                        Percent = 93,
                        Phase = "catalog",
                        Message = "Справочник: ставки из документа"
                    });
                    await catalog.SeedRatesFromDocumentAsync(doc);
                    await WriteEvent("progress", new ImportProgress
                    {
                        Percent = 96,
                        Phase = "catalog",
                        Message = "Справочник: аннотация коэффициентов"
                    });
                    await catalog.AnnotateAsync(doc);
                    await store.SaveAsync(doc);
                }
                catch (Exception ex)
                {
                    log.CreateLogger("import").LogWarning(ex, "Catalog annotate/seed skipped");
                    await WriteEvent("progress", new ImportProgress
                    {
                        Percent = 96,
                        Phase = "catalog",
                        Message = "Справочник пропущен"
                    });
                }

                await WriteEvent("progress", new ImportProgress
                {
                    Percent = 100,
                    Phase = "done",
                    Message = "Готово",
                    SheetCount = doc.Estimates.Count
                });
                await WriteEvent("done", doc);
            }
            catch (Exception ex)
            {
                log.CreateLogger("import").LogError(ex, "Excel import failed");
                try
                {
                    await WriteEvent("error", new { error = ex.Message });
                }
                catch
                {
                    /* response already closed */
                }
            }

            return Results.Empty;
        }).DisableAntiforgery();

        app.MapPost("/api/import/grand-xml", async (HttpRequest http, ProjectStore store) =>
        {
            var file = http.Form.Files.GetFile("file") ?? http.Form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "Нет файла XML/GSFX" });
            var ext = Path.GetExtension(file.FileName);
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
            await using (var fs = File.Create(tmp))
                await file.CopyToAsync(fs);
            try
            {
                var doc = GrandXml.LoadFile(tmp);
                store.EnsureFolder(doc);
                await store.SaveAsync(doc);
                return Results.Ok(doc);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* ignore */ }
            }
        }).DisableAntiforgery();

        app.MapPost("/api/export/grand-xml", async (ExportRequest req, ProjectStore store) =>
        {
            var doc = await store.GetAsync(req.ProjectId);
            if (doc is null) return Results.NotFound();
            var bytes = GrandXml.WriteUnsignedXml(doc, req.EstimateId);
            var name = Sanitize(doc.Project.ObjectName) + ".xml";
            return Results.File(bytes, "application/xml", name);
        });

        app.MapGet("/api/projects/{id}/export/grand-xml", async (string id, string? estimateId, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var bytes = GrandXml.WriteUnsignedXml(doc, estimateId);
            return Results.File(bytes, "application/xml", Sanitize(doc.Project.ObjectName) + ".xml");
        });

        app.MapGet("/api/projects/{id}/json", async (string id, ProjectStore store) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound();
            var json = PirJson.Serialize(doc);
            return Results.File(Encoding.UTF8.GetBytes(json), "application/json", Sanitize(doc.Project.ObjectName) + ".pir.json");
        });

        app.MapGet("/api/catalog/status", async (CatalogStore catalog) => Results.Ok(await catalog.StatusAsync()));
        app.MapGet("/api/catalog/koeffs", async (string? q, CatalogStore catalog) => Results.Ok(await catalog.SearchKoeffsAsync(q)));
        app.MapGet("/api/catalog/indexes", async (string? q, CatalogStore catalog) => Results.Ok(await catalog.SearchIndexesAsync(q)));
        app.MapGet("/api/catalog/rates", async (string? q, CatalogStore catalog) => Results.Ok(await catalog.SearchRatesAsync(q)));
        app.MapGet("/api/catalog/labor", (BookStore books) => Results.Ok(LaborRateCatalog.Read(books.Root)));
        app.MapPut("/api/catalog/labor/{catalogId}/posts/{postId}/role", (string catalogId, string postId, LaborRoleRequest req, BookStore books) =>
        {
            try
            {
                LaborRateCatalog.AssignRole(books.Root, catalogId, postId, req.RoleId);
                return Results.Ok(LaborRateCatalog.Read(books.Root));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPut("/api/catalog/labor/{catalogId}/posts/{postId}/link", (string catalogId, string postId, LaborLinkRequest req, BookStore books) =>
        {
            try
            {
                LaborRateCatalog.AssignLink(books.Root, catalogId, postId, req.CatalogId, req.PostId);
                return Results.Ok(LaborRateCatalog.Read(books.Root));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
        app.MapPost("/api/catalog/sync-apr", async (SyncAprRequest? req, CatalogStore catalog) =>
        {
            var r = await catalog.SyncAprAsync(req?.Directory);
            return r.Ok ? Results.Ok(r) : Results.BadRequest(r);
        });
        MapFolderAndCatalog(app);
        MapBooks(app);
        MapAnalogs(app);
        MapSources(app);
    }

    private static Estimate? FindEstimate(PirDocument doc, string estId) =>
        doc.Estimates.FirstOrDefault(e => e.Id == estId || e.Number == estId);

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(s) ? "estimate" : s;
    }
}

public sealed class PositionMoveRequest
{
    public string PositionId { get; set; } = "";
    public string? BeforeId { get; set; }
}

public sealed class SectionInsertRequest
{
    public string? BeforeId { get; set; }
}

public sealed class OutlineMoveRequest
{
    public string EstimateId { get; set; } = "";
    public string? TargetId { get; set; }
    public bool AsChild { get; set; }
}

public sealed class PatchProjectRequest
{
    public string? ObjectName { get; set; }
    public DesignStage? Stage { get; set; }
    public string? Designer { get; set; }
    public string? Customer { get; set; }
    public decimal? VatRate { get; set; }
    public decimal? CurrentIndex { get; set; }
    public RoundMode? RoundMode { get; set; }
    public KponPolicy? KponPolicy { get; set; }
    public string? Phase { get; set; }
    public List<ProjectCondition>? Conditions { get; set; }
}

public sealed class PatchEstimateRequest
{
    public string? Title { get; set; }
    public LaborModel? Labor { get; set; }
    public AsutpModel? Asutp { get; set; }
    public List<ProjectCondition>? Conditions { get; set; }
    public List<string>? SuppressedConditionIds { get; set; }
}

public sealed class PatchPositionRequest
{
    public decimal? Quantity { get; set; }
    public decimal? PriceA { get; set; }
    public decimal? PriceB { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public decimal? Qty { get; set; }
    public decimal? Xmin { get; set; }
    public decimal? Xmax { get; set; }
    public List<Factor>? Factors { get; set; }
    public List<string>? SuppressedConditionIds { get; set; }
    public List<IntervalItem>? Items { get; set; }
    public List<RelativeFactor>? RelativeFactors { get; set; }
}

public sealed class ExportRequest
{
    public string ProjectId { get; set; } = "";
    public string? EstimateId { get; set; }
}

public sealed class HeadingPlace
{
    public string? Title { get; set; }
    public string? BeforeId { get; set; }
    public List<string>? MemberIds { get; set; }
}

public sealed class OutlineLevelRequest
{
    public List<string> EstimateIds { get; set; } = [];
    public int Delta { get; set; }
}

public sealed class SyncAprRequest
{
    public string? Directory { get; set; }
}

public sealed class LaborRoleRequest
{
    public string? RoleId { get; set; }
}

public sealed class LaborLinkRequest
{
    public string CatalogId { get; set; } = "";
    public string PostId { get; set; } = "";
}
