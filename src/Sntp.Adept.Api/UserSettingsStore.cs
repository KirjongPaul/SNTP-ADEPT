using Sntp.Adept.Domain.Json;

namespace Sntp.Adept.Api;

/// <summary>
/// Личные настройки интерфейса. Ключ — ФИО из EXECUTERS, у каждого сотрудника свой блок.
/// </summary>
public sealed class UserSettingsStore(IConfiguration configuration, StaffDirectory staff)
{
    public const int PreviewMin = 320;
    public const int PreviewMax = 1200;
    private readonly object _gate = new();

    public string Root => string.IsNullOrWhiteSpace(configuration["Adept:BooksRoot"])
        ? BookStore.DefaultRoot
        : configuration["Adept:BooksRoot"]!.Trim();

    public int? PreviewWidth()
    {
        lock (_gate)
        {
            var file = Read();
            return file.Users.TryGetValue(Key(), out var ui) ? ui.PreviewWidth : null;
        }
    }

    public int SavePreviewWidth(int width)
    {
        var next = Math.Clamp(width, PreviewMin, PreviewMax);
        lock (_gate)
        {
            var file = Read();
            if (!file.Users.TryGetValue(Key(), out var ui))
            {
                ui = new UserUi();
                file.Users[Key()] = ui;
            }
            ui.PreviewWidth = next;
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "user-settings.json"), System.Text.Json.JsonSerializer.Serialize(file, PirJson.Options));
        }
        return next;
    }

    private string Key()
    {
        var fio = staff.CurrentFio();
        return string.IsNullOrWhiteSpace(fio) ? staff.WindowsAccount() : fio.Trim();
    }

    private UserSettingsFile Read()
    {
        var path = Path.Combine(Root, "user-settings.json");
        if (!File.Exists(path)) return new UserSettingsFile();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<UserSettingsFile>(File.ReadAllText(path), PirJson.Options)
                   ?? new UserSettingsFile();
        }
        catch
        {
            return new UserSettingsFile();
        }
    }

    private sealed class UserSettingsFile
    {
        public Dictionary<string, UserUi> Users { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class UserUi
    {
        public int? PreviewWidth { get; set; }
    }
}
