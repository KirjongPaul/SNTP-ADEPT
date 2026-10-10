using System.Text;
using System.Text.Json;
using Sntp.Adept.Domain.Documents;
using Sntp.Adept.Domain.Json;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Mcp;

/// <summary>stdio MCP: агент пишет черновики состава и ведомости, человек применяет их в UI.</summary>
public static class Program
{
    private static readonly JsonSerializerOptions Json = PirJson.Options;

    public static async Task Main()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        var stdin = Console.OpenStandardInput();
        while (true)
        {
            var message = await ReadMessage(stdin);
            if (message is null) return;
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            var id = root.TryGetProperty("id", out var idEl) ? idEl.Clone() : default;
            if (method is "notifications/initialized" or "notifications/cancelled") continue;
            var result = method switch
            {
                "initialize" => Init(),
                "tools/list" => Tools(),
                "tools/call" => Call(root),
                "ping" => new { },
                _ => null
            };
            if (id.ValueKind == JsonValueKind.Undefined) continue;
            object payload = result is null
                ? new { jsonrpc = "2.0", id, error = new { code = -32601, message = method ?? "" } }
                : new { jsonrpc = "2.0", id, result };
            WriteMessage(JsonSerializer.Serialize(payload, Json));
        }
    }

    private static object Init() => new
    {
        protocolVersion = "2024-11-05",
        capabilities = new { tools = new { } },
        serverInfo = new { name = "sntp-adept", version = "1.0" }
    };

    private static object Tools() => new
    {
        tools = new object[]
        {
            Tool("project_list", "Папки с project.json", "root"),
            Tool("estimates_rescan", "Перечитать estimates/*.json", "workFolder"),
            Tool("assignment_list", "Файлы задания и ТТ", "workFolder"),
            Tool("assignment_text", "Текст задания (docx/txt/pdf без OCR)", "workFolder", "name"),
            Tool("rpds_groups", "Группы состава работ для черновика", "workFolder"),
            Tool("scope_write_draft", "Записать черновик состава, не применяя его", "workFolder"),
            Tool("vor_write_draft", "Записать черновик ведомости объёмов", "workFolder"),
            Tool("draft_status", "Статус черновиков", "workFolder")
        }
    };

    private static object Tool(string name, string description, params string[] args) => new
    {
        name,
        description,
        inputSchema = new
        {
            type = "object",
            properties = args.ToDictionary(a => a, _ => new { type = "string" })
        }
    };

    private static object Call(JsonElement root)
    {
        var name = "";
        var args = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("params", out var p))
        {
            name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (p.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in a.EnumerateObject())
                    args[prop.Name] = prop.Value.Clone();
            }
        }

        try
        {
            var text = Invoke(name, args);
            return new { content = new[] { new { type = "text", text } }, isError = false };
        }
        catch (Exception ex)
        {
            return new { content = new[] { new { type = "text", text = ex.Message } }, isError = true };
        }
    }

    private static string Invoke(string name, Dictionary<string, JsonElement> args)
    {
        string Work() => Required(args, "workFolder");
        PirDocument Doc() => WorkFolder.Load(Work());
        return name switch
        {
            "project_list" => JsonSerializer.Serialize(Directory.Exists(Required(args, "root"))
                ? Directory.GetDirectories(Required(args, "root")).Where(d => File.Exists(Path.Combine(d, "project.json"))).ToArray()
                : []),
            "estimates_rescan" => JsonSerializer.Serialize(Doc().Estimates.Select(e => new { e.Id, e.Number, e.Title }), Json),
            "assignment_list" => JsonSerializer.Serialize(WorkFolder.ListAssignments(Doc())),
            "assignment_text" => WorkFolder.ReadAssignmentText(Doc(), Required(args, "name")),
            "rpds_groups" => JsonSerializer.Serialize(RpdGroups()),
            "scope_write_draft" => WriteScope(Doc(), args),
            "vor_write_draft" => WriteVor(Doc(), args),
            "draft_status" => Status(Doc()),
            _ => throw new InvalidOperationException("Неизвестный инструмент " + name)
        };
    }

    private static string WriteScope(PirDocument doc, Dictionary<string, JsonElement> args)
    {
        var draft = args.TryGetValue("draft", out var el)
            ? el.Deserialize<ScopeDraft>(Json) ?? new ScopeDraft()
            : new ScopeDraft();
        draft.Status = "draft";
        WorkFolder.WriteScopeDraft(doc, draft);
        return "черновик состава записан, в смету не внесён";
    }

    private static string WriteVor(PirDocument doc, Dictionary<string, JsonElement> args)
    {
        var draft = args.TryGetValue("draft", out var el)
            ? el.Deserialize<VorDraft>(Json) ?? new VorDraft()
            : new VorDraft();
        draft.Status = "draft";
        WorkFolder.WriteVorDraft(doc, draft);
        return "черновик ведомости записан, в смету не внесён";
    }

    private static string Status(PirDocument doc)
    {
        var scope = WorkFolder.ReadScopeDraft(doc);
        var vor = WorkFolder.ReadVorDrafts(doc);
        return JsonSerializer.Serialize(new
        {
            scope = scope?.Status ?? "none",
            scopeItems = scope?.Items.Count ?? 0,
            vor = vor.Select(v => new { v.Title, v.Status, rows = v.Rows.Count })
        }, Json);
    }

    private static IReadOnlyList<string> RpdGroups() =>
    [
        "Пояснительная записка",
        "Схема планировочной организации земельного участка",
        "Архитектурные решения",
        "Конструктивные и объёмно-планировочные решения",
        "Сведения об инженерном оборудовании",
        "Проект организации строительства",
        "Мероприятия по охране окружающей среды",
        "Мероприятия по обеспечению пожарной безопасности",
        "Смета на строительство",
        "Иная документация"
    ];

    private static string Required(Dictionary<string, JsonElement> args, string name)
    {
        if (!args.TryGetValue(name, out var el)) throw new InvalidOperationException("Нет аргумента " + name);
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText();
    }

    private static async Task<string?> ReadMessage(Stream stdin)
    {
        var header = new StringBuilder();
        var one = new byte[1];
        while (true)
        {
            var n = await stdin.ReadAsync(one);
            if (n == 0) return header.Length == 0 ? null : throw new InvalidOperationException("Обрыв заголовка MCP");
            header.Append((char)one[0]);
            if (header.Length >= 4 && header.ToString(header.Length - 4, 4) == "\r\n\r\n") break;
        }

        var length = 0;
        foreach (var line in header.ToString().Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.Split(':')[1].Trim(), out var len))
                length = len;
        }

        var buf = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stdin.ReadAsync(buf.AsMemory(read, length - read));
            if (n == 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    private static void WriteMessage(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Console.Write($"Content-Length: {bytes.Length}\r\n\r\n");
        Console.OpenStandardOutput().Write(bytes, 0, bytes.Length);
        Console.Out.Flush();
    }
}
