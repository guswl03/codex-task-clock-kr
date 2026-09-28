using System;
using System.Collections.Generic;
using System.IO;

internal static class Tests
{
    private static int failures;

    private static void Check(bool condition, string name)
    {
        if (!condition)
        {
            failures++;
            Console.WriteLine("실패: " + name);
        }
        else
        {
            Console.WriteLine("통과: " + name);
        }
    }

    private static TaskSnapshot Parse(string jsonl)
    {
        using (var reader = new StringReader(jsonl))
        {
            return SessionParser.Parse(reader, "sample", "예시 작업", new DateTime(2026, 9, 28, 3, 10, 0, DateTimeKind.Utc));
        }
    }

    public static int Main()
    {
        var active = Parse(
            "{\"timestamp\":\"2026-09-28T03:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"started_at\":\"2026-09-28T03:00:00Z\"}}\n" +
            "{\"timestamp\":\"2026-09-28T03:02:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"item_completed\",\"item\":{\"type\":\"CommandExecution\"}}}\n");
        Check(active.IsActive, "시작한 작업은 진행 중으로 표시");
        Check(active.StartedAtUtc == new DateTime(2026, 9, 28, 3, 0, 0, DateTimeKind.Utc), "시작 시각 보존");
        Check(active.LastActivity == "명령 실행", "활동을 한국어로 표시");

        var completed = Parse(
            "{\"timestamp\":\"2026-09-28T03:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\",\"started_at\":\"2026-09-28T03:00:00Z\"}}\n" +
            "{\"timestamp\":\"2026-09-28T03:12:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"completed_at\":\"2026-09-28T03:12:00Z\",\"duration_ms\":720000}}\n");
        Check(!completed.IsActive, "완료 이벤트가 진행 상태 종료");
        Check(completed.CompletedDurations.Count == 1 && completed.CompletedDurations[0] == TimeSpan.FromMinutes(12), "실제 완료 시간을 기록");

        var partial = Parse(
            "{\"timestamp\":\"2026-09-28T03:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n" +
            "{\"timestamp\":\"2026-09-28T03:01:00Z\",\"type\":\"event_msg\",\"payload\":");
        Check(partial.IsActive, "작성 중인 마지막 줄을 건너뜀");

        var history = new List<TimeSpan> {
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20),
            TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(40)
        };
        var estimate = TimeEstimate.FromHistory(TimeSpan.FromMinutes(8), history);
        Check(estimate.HasRange && estimate.MinRemaining == TimeSpan.FromMinutes(2) && estimate.MaxRemaining == TimeSpan.FromMinutes(22), "과거 25~75백분위로 남은 범위 계산");
        Check(TimeEstimate.FromHistory(TimeSpan.FromMinutes(31), history).ExceededTypicalRange, "평소 범위 초과 시 남은 시간을 단정하지 않음");
        Check(!TimeEstimate.FromHistory(TimeSpan.FromMinutes(2), history.GetRange(0, 3)).HasRange, "표본이 적으면 추정 생략");
        var longerHistory = new List<TimeSpan>();
        for (var minute = 10; minute <= 200; minute += 10)
            longerHistory.Add(TimeSpan.FromMinutes(minute));
        var conditional = TimeEstimate.FromHistory(TimeSpan.FromMinutes(153), longerHistory);
        Check(conditional.HasRange && conditional.IsConditional
            && conditional.MinRemaining == TimeSpan.FromMinutes(17)
            && conditional.MaxRemaining == TimeSpan.FromMinutes(37),
            "평소보다 길어져도 더 오래 걸린 과거 작업으로 다시 추정");

        var testRoot = Path.Combine(Path.GetTempPath(), "codex-clock-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sessions = Path.Combine(testRoot, "sessions", "2026", "09", "28");
            Directory.CreateDirectory(sessions);
            var id = "11111111-1111-1111-1111-111111111111";
            File.WriteAllText(Path.Combine(testRoot, "session_index.jsonl"),
                "{\"id\":\"" + id + "\",\"thread_name\":\"내 작업\"}\n");
            var log = Path.Combine(sessions, "rollout-2026-09-28T03-00-00-" + id + ".jsonl");
            File.WriteAllText(log,
                "{\"timestamp\":\"2026-09-28T03:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}\n");
            var store = new SessionStore(testRoot);
            var overview = store.Load(new DateTime(2026, 9, 28, 3, 2, 0, DateTimeKind.Utc));
            Check(overview.Recent.Count == 1 && overview.Recent[0].Title == "내 작업", "로컬 색인에서 제목을 연결");
            Check(overview.Recent[0].IsActive, "실제 파일에서 진행 상태 읽기");
            File.AppendAllText(log,
                "{\"timestamp\":\"2026-09-28T03:04:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\",\"duration_ms\":240000}}\n");
            overview = store.Load(new DateTime(2026, 9, 28, 3, 5, 0, DateTimeKind.Utc));
            Check(!overview.Recent[0].IsActive && overview.Recent[0].CompletedDurations.Count == 1,
                "파일이 늘어나면 완료 상태 갱신");
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }

        Console.WriteLine(failures == 0 ? "모든 검사 통과" : "검사 실패: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
