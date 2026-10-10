using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Calc;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Api;

public static partial class AdeptWebApp
{
    private static void MapBooks(WebApplication app)
    {
        app.MapGet("/api/books", (BookStore books) => Results.Ok(books.List()));

        app.MapGet("/api/books/offers", (BookStore books) =>
        {
            var hidden = books.HiddenOffers();
            var names = books.OfferNames();
            var groups = books.LoadAll().Select(book =>
            {
                var offers = EstimateOffers.From(book).Select(o =>
                {
                    var history = books.OfferHistory(o.Id);
                    var last = history.Count > 0 ? history[^1] : null;
                    return new
                    {
                        o.Id,
                        o.BookId,
                        o.BookTitle,
                        o.ClauseId,
                        o.Mark,
                        o.Text,
                        o.ShortTitle,
                        title = names.TryGetValue(o.Id, out var custom) && !string.IsNullOrWhiteSpace(custom) ? custom : o.ShortTitle,
                        hidden = hidden.Contains(o.Id),
                        changedBy = last?.UserName ?? "",
                        changedAt = last?.At ?? "",
                        history
                    };
                }).ToList();
                return new { bookId = book.Id, bookTitle = string.IsNullOrWhiteSpace(book.Title) ? book.Id : book.Title, offers };
            }).Where(g => g.offers.Count > 0);
            return Results.Ok(groups);
        });

        app.MapPost("/api/books/offers/hide", (OfferHideRequest req, BookStore books) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest(new { error = "Нет идентификатора" });
            books.SetOfferHidden(req.Id, req.Hidden);
            return Results.Ok(new { ok = true });
        });

        app.MapPost("/api/books/offers/name", (OfferNameRequest req, BookStore books, StaffDirectory staff) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest(new { error = "Нет идентификатора" });
            var fio = staff.CurrentFio();
            if (string.IsNullOrWhiteSpace(fio))
                return Results.BadRequest(new { error = "ФИО не найдено: процедура __GetExecutersRoleExtended не вернула сотрудника из dogovors.dbo.EXECUTERS" });
            books.SetOfferName(req.Id, req.Title, fio);
            return Results.Ok(new { ok = true });
        });

        app.MapGet("/api/books/{id}", (string id, BookStore books) =>
        {
            var book = books.Find(id);
            if (book is null) return Results.NotFound(new { error = "Сборник не найден" });
            var variables = FactorCatalog.ForBook(FactorCatalog.Build(books.LoadAll()), id);
            return Results.Ok(new { book, report = books.Report(id), variables, codexHref = CodexLinks.Href(book.Codex) });
        });

        app.MapGet("/api/books/{id}/user-notes", (string id, string? anchor, BookStore books) =>
        {
            if (books.Find(id) is null) return Results.NotFound(new { error = "Сборник не найден" });
            return Results.Ok(books.UserNotes(id, anchor));
        });

        app.MapGet("/api/me", (StaffDirectory staff) =>
        {
            var fio = staff.CurrentFio();
            return Results.Ok(new { userName = fio, account = staff.WindowsAccount(), resolved = !string.IsNullOrWhiteSpace(fio) });
        });

        app.MapGet("/api/me/settings", (UserSettingsStore settings) =>
            Results.Ok(new { previewWidth = settings.PreviewWidth() }));

        app.MapPut("/api/me/settings", (UserUiRequest req, UserSettingsStore settings) =>
        {
            if (req.PreviewWidth is not int width) return Results.BadRequest(new { error = "Нет ширины просмотра" });
            return Results.Ok(new { previewWidth = settings.SavePreviewWidth(width) });
        });

        app.MapPost("/api/books/{id}/user-notes", (string id, UserNoteRequest req, BookStore books, StaffDirectory staff) =>
        {
            if (books.Find(id) is null) return Results.NotFound(new { error = "Сборник не найден" });
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Пустое уточнение" });
            if (string.IsNullOrWhiteSpace(req.Anchor) || (!req.Anchor.StartsWith("item:") && !req.Anchor.StartsWith("table:")))
                return Results.BadRequest(new { error = "Уточнение привязывается к пункту или таблице" });
            var fio = staff.CurrentFio();
            if (string.IsNullOrWhiteSpace(fio))
                return Results.BadRequest(new { error = "ФИО не найдено: процедура __GetExecutersRoleExtended не вернула сотрудника из dogovors.dbo.EXECUTERS" });
            return Results.Ok(books.AddUserNote(id, req.Anchor, req.Text, req.Source ?? "", fio));
        });

        app.MapPatch("/api/books/{id}/user-notes/{noteId}", (string id, string noteId, UserNoteEdit req, BookStore books, StaffDirectory staff) =>
        {
            if (string.IsNullOrWhiteSpace(req.Text)) return Results.BadRequest(new { error = "Пустое уточнение" });
            var fio = staff.CurrentFio();
            if (string.IsNullOrWhiteSpace(fio))
                return Results.BadRequest(new { error = "ФИО не найдено: процедура __GetExecutersRoleExtended не вернула сотрудника из dogovors.dbo.EXECUTERS" });
            var note = books.EditUserNote(id, noteId, req.Text, fio);
            return note is null ? Results.NotFound(new { error = "Уточнение не найдено" }) : Results.Ok(note);
        });

        app.MapDelete("/api/books/{id}/user-notes/{noteId}", (string id, string noteId, BookStore books) =>
            books.DeleteUserNote(id, noteId) ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "Уточнение не найдено" }));

        app.MapGet("/api/books/{id}/notes", (string id, string? anchor, BookStore books) =>
        {
            var book = books.Find(id);
            if (book is null) return Results.NotFound(new { error = "Сборник не найден" });
            return Results.Ok(BookNotes.Lookup(book, anchor));
        });

        app.MapPost("/api/books/{id}/validate", (string id, BookStore books) =>
        {
            try { return Results.Ok(new { issues = books.Validate(id) }); }
            catch (InvalidOperationException ex) { return Results.NotFound(new { error = ex.Message }); }
        });

        app.MapPost("/api/books/{id}/diff", (string id, BookStore books) =>
        {
            try { return Results.Ok(books.Diff(id)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapPost("/api/books/sync", async (BookStore books, CancellationToken ct) =>
            Results.Ok(new { ok = true, books = await books.SyncAsync(ct) }));

        app.MapPost("/api/projects/{id}/book-check", async (string id, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            books.ApplyCheck(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/volumes/derive", async (string id, string estId, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound(new { error = "Смета не найдена" });
            est.Volumes = VolumeEngine.Derive(est, books.All());
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPatch("/api/projects/{id}/estimates/{estId}/volumes/{volId}", async (string id, string estId, string volId, VolumePatch req, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            var est = FindEstimate(doc, estId);
            var vol = est?.Volumes.FirstOrDefault(v => v.Id == volId);
            if (est is null || vol is null) return Results.NotFound(new { error = "Показатель не найден" });
            if (req.Quantity is decimal q) vol.Quantity = q;
            if (req.Quantity is not null)
            {
                VolumeEngine.Apply(est, books.All(), vol.Id);
                CalcEngine.Recalculate(doc);
            }
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/volumes/from-book", async (string id, string estId, VolumeFromBookRequest req, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound(new { error = "Смета не найдена" });
            var book = books.Find(req.BookId);
            if (book is null) return Results.NotFound(new { error = "Сборник не найден" });
            try
            {
                VolumeEngine.AddFromBook(est, book, req.Table, req.Item);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/survey/geology", async (string id, SurveyBuildRequest req, ProjectStore store) =>
        {
            if (SurveyGeology.IsProtectedProject(id))
                return Results.BadRequest(new { error = "На живой объект Степновской и на проект пробных объёмов справочник изысканий не записывается." });
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            var inputs = SurveyGeology.ReadInputs(doc.Survey);
            if (req.LengthM is not null) inputs.LengthM = req.LengthM;
            if (req.LayingDepthM is not null) inputs.LayingDepthM = req.LayingDepthM;
            if (req.FrostDepthM is not null) inputs.FrostDepthM = req.FrostDepthM;
            if (req.SpacingM is not null) inputs.SpacingM = req.SpacingM;
            if (req.DiameterMm is not null) inputs.DiameterMm = req.DiameterMm;
            if (req.SoilCategory is not null) inputs.SoilCategory = req.SoilCategory;
            doc.Survey = SurveyGeology.Compose(inputs, doc.Survey?.Parameters);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPatch("/api/projects/{id}/survey/geology/{paramId}", async (string id, string paramId, SurveyValueRequest req, ProjectStore store) =>
        {
            if (SurveyGeology.IsProtectedProject(id))
                return Results.BadRequest(new { error = "На живой объект Степновской и на проект пробных объёмов справочник изысканий не записывается." });
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            doc.Survey ??= SurveyGeology.Compose(new SurveyInputs());
            var row = doc.Survey.Parameters.FirstOrDefault(p => p.Id == paramId);
            if (row is null) return Results.NotFound(new { error = "Параметр не найден" });
            var inputs = SurveyGeology.ReadInputs(doc.Survey);
            if (row.Origin == "user")
            {
                if (paramId == "category") inputs.SoilCategory = req.Text;
                else if (req.Quantity is decimal q)
                {
                    if (paramId == "length") inputs.LengthM = q;
                    else if (paramId == "laying") inputs.LayingDepthM = q;
                    else if (paramId == "frost") inputs.FrostDepthM = q;
                    else if (paramId == "diameter") inputs.DiameterMm = q;
                }
                doc.Survey = SurveyGeology.Compose(inputs, doc.Survey.Parameters);
            }
            else
            {
                row.Manual = true;
                if (req.Quantity is decimal q) row.Quantity = q;
                row.QuantityTo = null;
                doc.Survey = SurveyGeology.Compose(inputs, doc.Survey.Parameters);
            }
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });

        app.MapPost("/api/projects/{id}/estimates/{estId}/volumes/apply", async (string id, string estId, ProjectStore store, BookStore books) =>
        {
            var doc = await store.GetAsync(id);
            if (doc is null) return Results.NotFound(new { error = "Объект не найден" });
            var est = FindEstimate(doc, estId);
            if (est is null) return Results.NotFound(new { error = "Смета не найдена" });
            if (est.Volumes.Count == 0) est.Volumes = VolumeEngine.Derive(est, books.All());
            VolumeEngine.Apply(est, books.All());
            CalcEngine.Recalculate(doc);
            await store.SaveAsync(doc);
            return Results.Ok(doc);
        });
    }
}

public sealed class VolumePatch
{
    public decimal? Quantity { get; set; }
    public List<string>? ChosenItems { get; set; }
}

public sealed class UserUiRequest
{
    public int? PreviewWidth { get; set; }
}

public sealed class UserNoteRequest
{
    public string Anchor { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Source { get; set; }
}

public sealed class UserNoteEdit
{
    public string Text { get; set; } = "";
}

public sealed class OfferHideRequest
{
    public string Id { get; set; } = "";
    public bool Hidden { get; set; }
}

public sealed class OfferNameRequest
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class VolumeFromBookRequest
{
    public string BookId { get; set; } = "";
    public string Table { get; set; } = "";
    public string Item { get; set; } = "";
}

public sealed class SurveyBuildRequest
{
    public decimal? LengthM { get; set; }
    public decimal? LayingDepthM { get; set; }
    public decimal? FrostDepthM { get; set; }
    public decimal? SpacingM { get; set; }
    public decimal? DiameterMm { get; set; }
    public string? SoilCategory { get; set; }
}

public sealed class SurveyValueRequest
{
    public decimal? Quantity { get; set; }
    public string? Text { get; set; }
}
