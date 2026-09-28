using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

internal sealed class TaskSnapshot
{
    public string Id { get; set; }
    public string Title { get; set; }
    public bool IsActive { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime LastActivityUtc { get; set; }
    public string LastActivity { get; set; }
    public int ToolCount { get; set; }
    public List<TimeSpan> CompletedDurations { get; private set; }

    public TaskSnapshot()
    {
        CompletedDurations = new List<TimeSpan>();
        LastActivity = "기록 확인 중";
    }
}

internal static class SessionParser
{
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 20000000 };

    public static TaskSnapshot Parse(TextReader reader, string id, string title, DateTime fallbackTimeUtc)
    {
        var task = new TaskSnapshot { Id = id, Title = title, LastActivityUtc = fallbackTimeUtc };
        DateTime? openStart = null;
        DateTime? lastEvent = null;
        string line;

        while ((line = reader.ReadLine()) != null)
        {
            Dictionary<string, object> entry;
            try { entry = Json.DeserializeObject(line) as Dictionary<string, object>; }
            catch (ArgumentException) { continue; }
            catch (InvalidOperationException) { continue; }
            if (entry == null) continue;

            var timestamp = ReadDate(entry, "timestamp");
            if (timestamp.HasValue) lastEvent = timestamp;
            if (ReadString(entry, "type") != "event_msg") continue;
            var payload = ReadObject(entry, "payload");
            var kind = ReadString(payload, "type");
            if (kind == "task_started")
            {
                openStart = ReadDate(payload, "started_at") ?? timestamp;
                task.StartedAtUtc = openStart;
                task.CompletedAtUtc = null;
                task.LastActivity = "작업 시작";
                task.ToolCount = 0;
            }
            else if (kind == "task_complete")
            {
                var ended = ReadDate(payload, "completed_at") ?? timestamp;
                var duration = ReadMilliseconds(payload, "duration_ms");
                if (!duration.HasValue && openStart.HasValue && ended.HasValue)
                    duration = ended.Value - openStart.Value;
                if (duration.HasValue && duration.Value > TimeSpan.Zero && duration.Value < TimeSpan.FromDays(2))
                    task.CompletedDurations.Add(duration.Value);
                task.CompletedAtUtc = ended;
                task.LastActivity = "작업 완료";
                openStart = null;
            }
            else if (kind == "item_completed" && openStart.HasValue)
            {
                var item = ReadObject(payload, "item");
                var itemType = ReadString(item, "type");
                if (itemType == "CommandExecution" || itemType == "McpToolCall" || itemType == "Extension")
                    task.ToolCount++;
                var label = ActivityLabel(itemType);
                if (label != null) task.LastActivity = label;
            }
        }

        task.IsActive = openStart.HasValue;
        task.LastActivityUtc = lastEvent ?? fallbackTimeUtc;
        return task;
    }

    private static string ActivityLabel(string itemType)
    {
        switch (itemType)
        {
            case "CommandExecution": return "명령 실행";
            case "McpToolCall": return "연결 도구 사용";
            case "Extension": return "자료 확인";
            case "AgentMessage": return "응답 작성";
            case "Reasoning": return "내용 검토";
            default: return null;
        }
    }

    internal static Dictionary<string, object> ReadObject(Dictionary<string, object> source, string key)
    {
        object value;
        return source != null && source.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
    }

    internal static string ReadString(Dictionary<string, object> source, string key)
    {
        object value;
        return source != null && source.TryGetValue(key, out value) && value != null
            ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
    }

    private static DateTime? ReadDate(Dictionary<string, object> source, string key)
    {
        var raw = ReadString(source, key);
        DateTime parsed;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : (DateTime?)null;
    }

    private static TimeSpan? ReadMilliseconds(Dictionary<string, object> source, string key)
    {
        var raw = ReadString(source, key);
        double milliseconds;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out milliseconds)
            && milliseconds >= 0 && milliseconds < TimeSpan.FromDays(2).TotalMilliseconds
            ? TimeSpan.FromMilliseconds(milliseconds) : (TimeSpan?)null;
    }
}

internal sealed class TimeEstimate
{
    public bool HasEstimate { get; private set; }
    public bool ExceededTypicalRange { get; private set; }
    public bool IsConditional { get; private set; }
    public TimeSpan Remaining { get; private set; }

    public static TimeEstimate FromHistory(TimeSpan elapsed, IList<TimeSpan> history)
    {
        var result = new TimeEstimate();
        if (history == null || history.Count < 5) return result;
        var sorted = history.Where(x => x >= TimeSpan.FromSeconds(30) && x <= TimeSpan.FromHours(4))
            .OrderBy(x => x).ToArray();
        if (sorted.Length < 5) return result;
        var high = Percentile(sorted, 0.75);
        var longer = sorted.Where(x => x > elapsed).ToArray();
        if (longer.Length < 3)
        {
            result.ExceededTypicalRange = elapsed >= high;
            return result;
        }
        result.HasEstimate = true;
        result.IsConditional = elapsed >= high;
        result.Remaining = Percentile(longer, 0.5) - elapsed;
        return result;
    }

    private static TimeSpan Percentile(TimeSpan[] sorted, double fraction)
    {
        var position = (sorted.Length - 1) * fraction;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var ticks = sorted[lower].Ticks + (long)((sorted[upper].Ticks - sorted[lower].Ticks) * (position - lower));
        return TimeSpan.FromTicks(ticks);
    }
}
