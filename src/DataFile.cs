// State files (curves, backup, turbo, sessions, machine) are written atomically and read tolerantly: a power cut in
// the middle of a write, or a damaged file, must never keep FanPilot from starting.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

static class DataFile
{
    static readonly Encoding Utf8 = new UTF8Encoding(false);

    // The new content goes to path.tmp, flushed to disk, then replaces path in one step; the previous version stays in
    // path.bak. A crash leaves either the old file or the new one, never half of one.
    public static void WriteAtomic(string path, object value)
    {
        string tmp = path + ".tmp";
        var bytes = Utf8.GetBytes(Json.Serialize(value));
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        Retry(() =>
        {
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
            else File.Move(tmp, path);
        });
    }

    // The object stored in path, or null when there is none. A damaged file is moved aside (path.corrompido-<date>),
    // so the next write cannot turn the good .bak into a copy of it, and the previous version (path.bak) is used
    // instead when it is readable. Either way the problem goes to the log and to the panel.
    public static Dictionary<string, object> Read(string path)
    {
        if (!File.Exists(path)) return null; // deleted on purpose: start fresh, do not resurrect the .bak
        string problem;
        var value = TryParse(path, out problem);
        if (value != null) return value;
        object aside = MoveAside(path);
        string ignored;
        var previous = File.Exists(path + ".bak") ? TryParse(path + ".bak", out ignored) : null;
        AppNotices.Add(new Msg(previous != null ? "notice.fileUnreadable.previous" : "notice.fileUnreadable.fresh",
                               "file", Path.GetFileName(path), "problem", problem, "aside", aside));
        return previous;
    }

    // Unreadable content counts as damage; a file that cannot be opened (held by another program) does not, and the
    // error propagates after a few attempts rather than losing a good file.
    static Dictionary<string, object> TryParse(string path, out string problem)
    {
        string text = null;
        Retry(() => text = File.ReadAllText(path));
        try
        {
            problem = null;
            return Json.ParseObject(text);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidOperationException)
        {
            problem = ex.Message;
            return null;
        }
    }

    // The name it was set aside as, or (it could not be moved) a message saying so, for the notice.
    static object MoveAside(string path)
    {
        string aside = path + ".corrompido-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        try
        {
            File.Move(path, aside);
            return Path.GetFileName(aside);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return new Msg("notice.notMoved", "file", Path.GetFileName(path), "detail", ex.Message);
        }
    }

    // OneDrive, an antivirus or an editor can hold a file for a moment.
    static void Retry(Action io)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                io();
                return;
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 3)
            {
                Thread.Sleep(100);
            }
        }
    }
}
