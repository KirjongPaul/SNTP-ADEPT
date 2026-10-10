using System.Runtime.InteropServices;
using System.Text;

namespace Sntp.Adept.Domain.Documents;

/// <summary>
/// Папка на диске остаётся с устойчивым именем (код аналога, sources).
/// Проводник показывает обычное имя из desktop.ini.
/// Чтобы он его прочитал, у папки должны стоять ReadOnly и System, а у desktop.ini — Hidden и System.
/// Одного ReadOnly на Windows 11 уже недостаточно: вложенные папки с коротким кодом остаются «системными».
/// </summary>
public static class ShellFolder
{
    const int UpdateItem = 0x00002000;
    const int UpdateDir = 0x00001000;
    const int AttributesChanged = 0x00000800;
    const uint PathAndFlush = 0x0005 | 0x2000;

    public static void ShowAs(string dir, string name, string tip)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var body = "[.ShellClassInfo]\r\n"
                       + "LocalizedResourceName=" + Line(name) + "\r\n"
                       + "InfoTip=" + Line(tip) + "\r\n"
                       + "ConfirmFileOp=0\r\n";
            var ini = Path.Combine(dir, "desktop.ini");
            if (!Same(ini, body))
            {
                if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                File.WriteAllText(ini, body, new UnicodeEncoding(false, true));
                File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            }
            var attrs = File.GetAttributes(dir);
            var marked = FileAttributes.ReadOnly | FileAttributes.System;
            if ((attrs & marked) != marked)
                File.SetAttributes(dir, attrs | marked);
            Notify(dir);
        }
        catch (IOException)
        {
            /* проводник держит desktop.ini: имя папки не обновляем, проект всё равно открывается */
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool Same(string ini, string body)
    {
        try
        {
            if (!File.Exists(ini)) return false;
            return File.ReadAllText(ini) == body;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static void Release(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var attrs = File.GetAttributes(dir);
        attrs &= ~(FileAttributes.ReadOnly | FileAttributes.System);
        File.SetAttributes(dir, attrs);
        var ini = Path.Combine(dir, "desktop.ini");
        if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
    }

    private static string Line(string value)
    {
        var text = (value ?? "").Replace("\r", " ").Replace("\n", " ").Replace("=", " ").Trim();
        if (text.StartsWith('@')) text = text[1..].Trim();
        return text.Length == 0 ? "Без названия" : text;
    }

    private static void Notify(string dir)
    {
        if (!OperatingSystem.IsWindows()) return;
        var parent = Path.GetDirectoryName(dir);
        SHChangeNotify(AttributesChanged, PathAndFlush, dir, null);
        SHChangeNotify(UpdateItem, PathAndFlush, dir, null);
        if (!string.IsNullOrEmpty(parent))
            SHChangeNotify(UpdateDir, PathAndFlush, parent, null);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, string? item1, string? item2);
}
