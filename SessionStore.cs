using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class SessionOverview
{
    public List<TaskSnapshot> Recent { get; set; }
    public List<TimeSpan> HistoricalDurations { get; set; }
}

internal sealed class SessionStore
{
    private sealed class CachedFile
    {
        public long Length;
        public DateTime WrittenUtc;
        public TaskSnapshot Snapshot;
    }

    private readonly string root;
    private readonly Dictionary<string, CachedFile> cache = new Dictionary<string, CachedFile>(StringComparer.OrdinalIgnoreCase);
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 20000000 };

    public SessionStore(string rootDirectory)
    {
        root = rootDirectory;
    }

    public static string DefaultRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !String.IsNullOrWhiteSpace(configured)
            ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    public SessionOverview Load(DateTime nowUtc)
    {
        var result = new SessionOverview {
            Recent = new List<TaskSnapshot>(),
            HistoricalDurations = new List<TimeSpan>()
        };
        var sessionsPath = Path.Combine(root, "sessions");
        if (!Directory.Exists(sessionsPath)) return result;

        var titles = ReadTitles();
        FileInfo[] files;
        try
        {
            files = Directory.EnumerateFiles(sessionsPath, "rollout-*.jsonl", SearchOption.AllDirectories)
                .Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).Take(80).ToArray();
        }
        catch (IOException) { return result; }
        catch (UnauthorizedAccessException) { return result; }

        foreach (var file in files)
        {
            var id = ReadId(file.Name);
            if (id == null) continue;
            CachedFile cached;
            TaskSnapshot task;
            if (cache.TryGetValue(file.FullName, out cached)
                && cached.Length == file.Length && cached.WrittenUtc == file.LastWriteTimeUtc)
            {
                task = cached.Snapshot;
            }
            else
            {
                try
                {
                    using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                        task = SessionParser.Parse(reader, id, "이름 없는 작업", file.LastWriteTimeUtc);
                    cache[file.FullName] = new CachedFile {
                        Length = file.Length, WrittenUtc = file.LastWriteTimeUtc, Snapshot = task
                    };
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
            }
            string title;
            task.Title = titles.TryGetValue(id, out title) && !String.IsNullOrWhiteSpace(title)
                ? title : "이름 없는 작업";
            result.Recent.Add(task);
            result.HistoricalDurations.AddRange(task.CompletedDurations);
        }

        result.Recent = result.Recent
            .OrderByDescending(x => x.IsActive && nowUtc - x.LastActivityUtc < TimeSpan.FromHours(1))
            .ThenByDescending(x => x.LastActivityUtc).Take(5).ToList();
        return result;
    }

    private Dictionary<string, string> ReadTitles()
    {
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(root, "session_index.jsonl");
        if (!File.Exists(path)) return titles;
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Dictionary<string, object> entry;
                    try { entry = json.DeserializeObject(line) as Dictionary<string, object>; }
                    catch (ArgumentException) { continue; }
                    catch (InvalidOperationException) { continue; }
                    var id = SessionParser.ReadString(entry, "id");
                    var title = SessionParser.ReadString(entry, "thread_name");
                    if (!String.IsNullOrWhiteSpace(id) && title != null) titles[id] = title;
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return titles;
    }

    private static string ReadId(string filename)
    {
        var stem = Path.GetFileNameWithoutExtension(filename);
        if (stem.Length < 36) return null;
        var id = stem.Substring(stem.Length - 36);
        Guid guid;
        return Guid.TryParse(id, out guid) ? id : null;
    }
}
