using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Sntp.Adept.Domain.Books;
using Sntp.Adept.Domain.Json;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Api;

public sealed class BookStore(IConfiguration configuration)
{
    public const string DefaultRoot = @"D:\NET\=SNTP-BOOKS";

    public string Root => string.IsNullOrWhiteSpace(configuration["Adept:BooksRoot"])
        ? DefaultRoot
        : configuration["Adept:BooksRoot"]!.Trim();

    public IReadOnlyList<BookSummary> List()
    {
        if (!Directory.Exists(Root)) return [];
        var list = new List<BookSummary>();
        foreach (var dir in Directory.GetDirectories(Root))
        {
            var path = Path.Combine(dir, "book.json");
            if (!File.Exists(path)) continue;
            try
            {
                var book = Read(path);
                list.Add(Summarize(book, dir));
            }
            catch
            {
                list.Add(new BookSummary(Path.GetFileName(dir), "", Path.GetFileName(dir), "", "broken", dir, 0, 0, 0));
            }
        }
        return list.OrderBy(b => b.Title).ToList();
    }

    public List<UserBookNote> UserNotes(string bookId, string? anchor)
    {
        var file = ReadUserNotes();
        return file.Notes
            .Where(n => string.Equals(n.BookId, bookId, StringComparison.OrdinalIgnoreCase))
            .Where(n => string.IsNullOrWhiteSpace(anchor) || string.Equals(n.Anchor, anchor, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.CreatedAt)
            .ToList();
    }

    public UserBookNote AddUserNote(string bookId, string anchor, string text, string source, string userName)
    {
        var file = ReadUserNotes();
        var now = DateTimeOffset.UtcNow.ToString("o");
        var note = new UserBookNote
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            BookId = bookId,
            Anchor = anchor.Trim(),
            Text = text.Trim(),
            Source = source.Trim(),
            UserName = userName.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        };
        file.Notes.Add(note);
        WriteUserNotes(file);
        return note;
    }

    public UserBookNote? EditUserNote(string bookId, string noteId, string text, string userName)
    {
        var file = ReadUserNotes();
        var note = file.Notes.FirstOrDefault(n =>
            string.Equals(n.BookId, bookId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(n.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (note is null) return null;
        var next = text.Trim();
        if (next.Length == 0 || next == note.Text) return note;
        note.History.Add(new UserNoteRevision
        {
            At = note.UpdatedAt.Length > 0 ? note.UpdatedAt : note.CreatedAt,
            UserName = note.UserName,
            Text = note.Text
        });
        note.Text = next;
        note.UserName = userName.Trim();
        note.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
        WriteUserNotes(file);
        return note;
    }

    public bool DeleteUserNote(string bookId, string noteId)
    {
        var file = ReadUserNotes();
        var removed = file.Notes.RemoveAll(n =>
            string.Equals(n.BookId, bookId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(n.Id, noteId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        WriteUserNotes(file);
        return true;
    }

    private UserNotesFile ReadUserNotes()
    {
        var path = Path.Combine(Root, "user-notes.json");
        if (!File.Exists(path)) return new UserNotesFile();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<UserNotesFile>(File.ReadAllText(path), PirJson.Options)
                   ?? new UserNotesFile();
        }
        catch
        {
            return new UserNotesFile();
        }
    }

    private void WriteUserNotes(UserNotesFile file)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, "user-notes.json"), System.Text.Json.JsonSerializer.Serialize(file, PirJson.Options));
    }

    public HashSet<string> HiddenOffers() =>
        new(ReadOfferFile().Hidden, StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> OfferNames() =>
        ReadOfferFile().Names;

    public void SetOfferHidden(string id, bool hidden)
    {
        var file = ReadOfferFile();
        if (hidden)
        {
            if (!file.Hidden.Contains(id, StringComparer.Ordinal)) file.Hidden.Add(id);
        }
        else file.Hidden.RemoveAll(x => string.Equals(x, id, StringComparison.Ordinal));
        WriteOfferFile(file);
    }

    public void SetOfferName(string id, string? name, string userName)
    {
        var file = ReadOfferFile();
        var title = (name ?? "").Trim();
        file.Names.TryGetValue(id, out var current);
        if (string.Equals(title, (current ?? "").Trim(), StringComparison.Ordinal)) return;
        if (!file.History.TryGetValue(id, out var log))
        {
            log = [];
            file.History[id] = log;
        }
        if (title.Length == 0) file.Names.Remove(id);
        else file.Names[id] = title;
        log.Add(new OfferNameChange
        {
            At = DateTimeOffset.UtcNow.ToString("o"),
            UserName = userName.Trim(),
            Title = title
        });
        WriteOfferFile(file);
    }

    public IReadOnlyList<OfferNameChange> OfferHistory(string id)
    {
        var file = ReadOfferFile();
        return file.History.TryGetValue(id, out var log) ? log : [];
    }

    private OfferHideFile ReadOfferFile()
    {
        var path = Path.Combine(Root, "offer-hide.json");
        if (!File.Exists(path)) return new OfferHideFile();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<OfferHideFile>(File.ReadAllText(path), PirJson.Options)
                   ?? new OfferHideFile();
        }
        catch
        {
            return new OfferHideFile();
        }
    }

    private void WriteOfferFile(OfferHideFile file)
    {
        Directory.CreateDirectory(Root);
        file.Hidden = file.Hidden.Distinct(StringComparer.Ordinal).OrderBy(x => x).ToList();
        File.WriteAllText(Path.Combine(Root, "offer-hide.json"), System.Text.Json.JsonSerializer.Serialize(file, PirJson.Options));
    }

    public IReadOnlyList<ReferenceBook> LoadAll()
    {
        if (!Directory.Exists(Root)) return [];
        var list = new List<ReferenceBook>();
        foreach (var dir in Directory.GetDirectories(Root))
        {
            var path = Path.Combine(dir, "book.json");
            if (!File.Exists(path)) continue;
            try { list.Add(Read(path)); }
            catch { /* сломанный сборник не входит в каталог факторов */ }
        }
        return list;
    }

    public ReferenceBook? Find(string id)
    {
        foreach (var summary in List())
        {
            if (!string.Equals(summary.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(summary.Folder, "book.json");
            return File.Exists(path) ? Read(path) : null;
        }
        return null;
    }

    public string? Report(string id)
    {
        var book = Find(id);
        if (book?.Folder is null) return null;
        var path = Path.Combine(book.Folder, "report.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public IReadOnlyList<string> Validate(string id)
    {
        var book = Find(id) ?? throw new InvalidOperationException("Сборник не найден");
        return BookValidator.Check(book);
    }

    public IReadOnlyList<BookFieldDiff> Diff(string id)
    {
        var book = Find(id) ?? throw new InvalidOperationException("Сборник не найден");
        var checkPath = Path.Combine(book.Folder ?? "", "work", "check.json");
        if (!File.Exists(checkPath)) throw new InvalidOperationException("Нет work/check.json");
        var check = Read(checkPath);
        return BookDiff.Compare(book, check);
    }

    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Root);
        var books = List().Where(s => s.Status != "broken").Select(s => Read(Path.Combine(s.Folder, "book.json"))).ToList();
        await using var connection = Open();
        connection.Open();
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var book in books)
        {
            var json = PirJson.Options;
            var body = System.Text.Json.JsonSerializer.Serialize(book, json);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM dbo.AdeptBookNotes WHERE BookId=@Id;
                DELETE FROM dbo.AdeptBookItems WHERE BookId=@Id;
                DELETE FROM dbo.AdeptBooks WHERE Id=@Id;
                INSERT INTO dbo.AdeptBooks (Id, Kind, Title, OrderRef, Status, Folder, BookJson, FileHash, UpdatedAt)
                VALUES (@Id, @Kind, @Title, @OrderRef, @Status, @Folder, @BookJson, @FileHash, SYSUTCDATETIME());
                """,
                new
                {
                    book.Id,
                    book.Kind,
                    book.Title,
                    book.OrderRef,
                    book.Status,
                    book.Folder,
                    BookJson = body,
                    FileHash = hash
                },
                transaction: tx,
                cancellationToken: cancellationToken));
            foreach (var table in book.Tables.Where(t => t.Kind == "price"))
            foreach (var item in table.Items)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptBookItems
                    (BookId, TableCode, Item, Name, ParamUnit, Xmin, Xmax, PriceA, PriceB, CheckStatus, Page)
                    VALUES (@BookId, @TableCode, @Item, @Name, @ParamUnit, @Xmin, @Xmax, @PriceA, @PriceB, @CheckStatus, @Page)
                    """,
                    new
                    {
                        BookId = book.Id,
                        TableCode = table.Code,
                        Item = item.N,
                        Name = item.Name.Length > 1000 ? item.Name[..1000] : item.Name,
                        ParamUnit = item.Unit ?? table.ParamUnit,
                        item.Xmin,
                        item.Xmax,
                        PriceA = item.A,
                        PriceB = item.B,
                        CheckStatus = item.Check,
                        Page = item.Src?.Page
                    },
                    transaction: tx,
                    cancellationToken: cancellationToken));
            }
            foreach (var note in book.Notes)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO dbo.AdeptBookNotes (BookId, Anchor, LetterRef, Quote, Page, FileName)
                    VALUES (@BookId, @Anchor, @LetterRef, @Quote, @Page, @FileName)
                    """,
                    new
                    {
                        BookId = book.Id,
                        note.Anchor,
                        LetterRef = note.LetterRef.Length > 300 ? note.LetterRef[..300] : note.LetterRef,
                        Quote = note.Quote,
                        note.Page,
                        FileName = note.File
                    },
                    transaction: tx,
                    cancellationToken: cancellationToken));
            }
        }
        await tx.CommitAsync(cancellationToken);
        return books.Count;
    }

    public void ApplyCheck(PirDocument doc)
    {
        var books = All();
        doc.Issues.RemoveAll(i => i.Code.StartsWith("book-", StringComparison.Ordinal));
        foreach (var hit in BookReconciler.Check(doc, books))
        {
            doc.Issues.Add(new Clarification
            {
                Severity = hit.Status is "book-value-mismatch" or "book-ref-mismatch" ? ClarificationSeverity.Warning : ClarificationSeverity.Info,
                Code = hit.Status,
                Message = hit.Message,
                EstimateId = hit.EstimateId,
                PositionId = hit.PositionId,
                Field = "basis"
            });
        }
    }

    public IReadOnlyList<ReferenceBook> All() =>
        List().Where(s => s.Status != "broken").Select(s => Read(Path.Combine(s.Folder, "book.json"))).ToList();

    public static ReferenceBook Read(string path)
    {
        var book = System.Text.Json.JsonSerializer.Deserialize<ReferenceBook>(File.ReadAllText(path), PirJson.Options)
                   ?? throw new InvalidOperationException("Пустой book.json");
        book.Folder = Path.GetDirectoryName(path);
        return book;
    }

    public static void Write(ReferenceBook book, string folder)
    {
        Directory.CreateDirectory(folder);
        book.Folder = folder;
        File.WriteAllText(Path.Combine(folder, "book.json"), System.Text.Json.JsonSerializer.Serialize(book, PirJson.Options));
    }

    private static BookSummary Summarize(ReferenceBook book, string folder) => new(
        book.Id,
        book.Kind,
        book.Title,
        book.OrderRef,
        book.Status,
        folder,
        book.Tables.Count(t => t.Kind == "price"),
        book.Tables.Where(t => t.Kind == "price").Sum(t => t.Items.Count),
        book.Notes.Count);

    private SqlConnection Open()
    {
        var connectionString = configuration.GetConnectionString("SNTP_PPO");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Connection string SNTP_PPO is not configured.");
        return new SqlConnection(connectionString);
    }
}

public sealed class UserBookNote
{
    public string Id { get; set; } = "";
    public string BookId { get; set; } = "";
    public string Anchor { get; set; } = "";
    public string Text { get; set; } = "";
    public string Source { get; set; } = "";
    public string UserName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public List<UserNoteRevision> History { get; set; } = [];
}

public sealed class UserNoteRevision
{
    public string At { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class UserNotesFile
{
    public List<UserBookNote> Notes { get; set; } = [];
}

public sealed class OfferNameChange
{
    public string At { get; set; } = "";
    public string UserName { get; set; } = "";
    public string Title { get; set; } = "";
}

public sealed class OfferHideFile
{
    public List<string> Hidden { get; set; } = [];
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<OfferNameChange>> History { get; set; } = new(StringComparer.Ordinal);
}

public sealed record BookSummary(
    string Id,
    string Kind,
    string Title,
    string OrderRef,
    string Status,
    string Folder,
    int Tables,
    int Items,
    int Notes);
