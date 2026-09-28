using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

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

    [STAThread]
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
        Check(estimate.HasEstimate && estimate.Remaining == TimeSpan.FromMinutes(17),
            "현재까지 걸린 시간보다 긴 완료 작업의 중앙값으로 한 숫자를 계산");
        var inProgress = TimeEstimate.FromHistory(TimeSpan.FromMinutes(18), history);
        Check(inProgress.HasEstimate && inProgress.Remaining == TimeSpan.FromMinutes(12),
            "중앙값보다 오래 진행돼도 남은 시간을 0분으로 단정하지 않음");
        Check(TimeEstimate.FromHistory(TimeSpan.FromMinutes(31), history).ExceededTypicalRange, "평소 범위 초과 시 남은 시간을 단정하지 않음");
        Check(!TimeEstimate.FromHistory(TimeSpan.FromMinutes(2), history.GetRange(0, 3)).HasEstimate, "표본이 적으면 추정 생략");
        var longerHistory = new List<TimeSpan>();
        for (var minute = 10; minute <= 200; minute += 10)
            longerHistory.Add(TimeSpan.FromMinutes(minute));
        var conditional = TimeEstimate.FromHistory(TimeSpan.FromMinutes(153), longerHistory);
        Check(conditional.HasEstimate && conditional.IsConditional
            && conditional.Remaining == TimeSpan.FromMinutes(27),
            "평소보다 길어져도 더 오래 걸린 과거 작업의 중앙값으로 다시 추정");

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

            var previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            Environment.SetEnvironmentVariable("CODEX_HOME", testRoot);
            try
            {
                using (var form = new ClockForm())
                {
                    var heading = form.Controls.OfType<Label>().Single(x => x.Text == "코덱스 작업 시계");
                    var refresh = form.Controls.OfType<Button>().Single();
                    var card = form.Controls.OfType<Panel>().Single(x => x.GetType() == typeof(Panel));
                    var list = form.Controls.OfType<FlowLayoutPanel>().Single();
                    var listHeading = form.Controls.OfType<Label>().Single(x => x.Text.StartsWith("최근 작업"));
                    var etaValue = card.Controls.OfType<Label>().Single(x => x.Text == "계산 중");
                    Check(form.ClientSize.Width <= 380 && form.ClientSize.Height <= 450,
                        "기본 창은 작은 위젯 크기");
                    Check(!heading.Bounds.IntersectsWith(refresh.Bounds),
                        "제목과 새로고침 버튼이 겹치지 않음");
                    Check(card.Height <= 205 && card.Bottom < list.Top && list.Bottom <= form.ClientSize.Height - 20,
                        "현재 작업과 최근 목록을 작은 창 안에 배치");
                    Check(heading.Font.Size <= 16f && card.Controls.OfType<Label>().Max(x => x.Font.Size) <= 24f,
                        "제목과 경과시간 글자를 작은 창에 맞게 축소");
                    Check(form.BackColor.GetSaturation() < 0.18f && card.BackColor.GetSaturation() < 0.18f,
                        "배경과 카드에 차분한 무채색 사용");
                    Check(form.ClientSize.Width <= 345 && form.ClientSize.Height <= 390,
                        "창 크기를 더 줄임");
                    Check(heading.Font.Size <= 12f && card.Controls.OfType<Label>().Max(x => x.Font.Size) <= 19f,
                        "전반적인 글자 크기를 줄임");
                    Check(form.BackColor.GetBrightness() > 0.85f && card.BackColor.GetBrightness() > 0.9f
                        && form.ForeColor.GetBrightness() < 0.25f,
                        "밝은 바탕과 짙은 글자로 변경");
                    Check(listHeading.Text == "최근 작업",
                        "최근 목록 안내 문구를 간결하게 표시");
                    Check(form.BackColor.GetBrightness() > card.BackColor.GetBrightness(),
                        "코덱스 화면처럼 흰 바탕과 옅은 카드 구분");
                    Check(refresh.BackColor == form.BackColor,
                        "새로고침 버튼에 진한 채우기 색을 쓰지 않음");
                    Check(etaValue.ForeColor == form.ForeColor && heading.Font.Size <= 10f,
                        "남은 시간과 제목은 절제된 본문 스타일로 표시");
                    using (var bitmap = new Bitmap(card.Width, card.Height))
                    {
                        card.DrawToBitmap(bitmap, card.ClientRectangle);
                        Check(bitmap.GetPixel(0, card.Height / 2).GetBrightness() > 0.85f
                            && bitmap.GetPixel(0, card.Height / 2).GetBrightness() < card.BackColor.GetBrightness(),
                            "현재 작업 카드에 옅은 경계선 표시");
                    }
                    form.Size = form.MinimumSize;
                    Check(card.Bottom < list.Top && list.Bottom <= form.ClientSize.Height - 20,
                        "최소 창 크기에서도 내용이 겹치지 않음");
                    Check(refresh.ClientSize.Width >= TextRenderer.MeasureText(refresh.Text, refresh.Font).Width + 24
                        && heading.ClientSize.Width >= TextRenderer.MeasureText(heading.Text, heading.Font).Width,
                        "최소 창 크기에서도 제목과 새로고침 글자가 온전히 표시됨");
                    Check(form.ClientSize.Width - refresh.Right >= 24,
                        "최소 창 크기에서도 새로고침 버튼 오른쪽에 충분한 여백이 남음");
                    Check(TextRenderer.MeasureText(form.Text, SystemFonts.CaptionFont).Width <= 80,
                        "제목 표시줄의 이름이 작은 창에 맞게 표시됨");

                    var activeOverview = new SessionOverview {
                        Recent = new List<TaskSnapshot> {
                            new TaskSnapshot { Id = "active", Title = "예측 검사", IsActive = true,
                                StartedAtUtc = DateTime.UtcNow - TimeSpan.FromMinutes(8),
                                LastActivityUtc = DateTime.UtcNow }
                        },
                        HistoricalDurations = history
                    };
                    typeof(ClockForm).GetField("overview", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(form, activeOverview);
                    typeof(ClockForm).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, null);
                    Check(etaValue.Text == "약 17분", "남은 시간을 범위 대신 한 숫자로 표시");
                    activeOverview.Recent[0].StartedAtUtc = DateTime.UtcNow - TimeSpan.FromMinutes(153);
                    activeOverview.HistoricalDurations = longerHistory;
                    typeof(ClockForm).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, null);
                    var estimateTitle = (Label)typeof(ClockForm).GetField("etaTitle",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Check(estimateTitle.Text == "오래 걸린 기록 기준 · 추정",
                        "오래 진행된 작업은 계산에 사용한 기록을 정확히 안내");

                    const string longTitle = "아주 긴 코덱스 작업 제목 전체 보기";
                    const string longRecentTitle = "최근 작업의 긴 제목 전체 보기";
                    var testOverview = new SessionOverview {
                        Recent = new List<TaskSnapshot> {
                            new TaskSnapshot { Id = "primary", Title = longTitle, LastActivityUtc = DateTime.UtcNow },
                            new TaskSnapshot { Id = "recent", Title = longRecentTitle, LastActivityUtc = DateTime.UtcNow }
                        },
                        HistoricalDurations = new List<TimeSpan>()
                    };
                    typeof(ClockForm).GetField("overview", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(form, testOverview);
                    typeof(ClockForm).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, null);
                    var titleLabel = card.Controls.OfType<Label>().Single(x => x.Text == longTitle);
                    var recentName = list.Controls.OfType<Panel>().Single().Controls.OfType<Label>().First();
                    var tipField = typeof(ClockForm).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                        .FirstOrDefault(x => typeof(ToolTip).IsAssignableFrom(x.FieldType));
                    var titleTip = tipField == null ? null : tipField.GetValue(form) as ToolTip;
                    Check(titleTip != null && titleTip.GetToolTip(titleLabel) == longTitle
                        && titleTip.GetToolTip(recentName) == longRecentTitle,
                        "긴 작업 제목은 마우스를 올리면 전체를 볼 수 있음");
                    form.Controls.OfType<CheckBox>().Single(x => x.Text == "제목 숨김").Checked = true;
                    Check(titleTip != null && titleTip.GetToolTip(titleLabel) != longTitle,
                        "제목 숨김 상태에서는 설명에도 실제 제목이 노출되지 않음");

                    form.CreateControl();
                    var keyMessage = Message.Create(form.Handle, 0x100, (IntPtr)Keys.W, IntPtr.Zero);
                    var processCmdKey = typeof(ClockForm).GetMethod("ProcessCmdKey",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var shortcutHandled = processCmdKey != null && (bool)processCmdKey.Invoke(form,
                        new object[] { keyMessage, Keys.Control | Keys.W });
                    Check(shortcutHandled && form.IsDisposed, "Ctrl+W를 누르면 창이 닫힘");
                }
            }
            finally { Environment.SetEnvironmentVariable("CODEX_HOME", previousHome); }
        }
        finally
        {
            if (Directory.Exists(testRoot)) Directory.Delete(testRoot, true);
        }

        Console.WriteLine(failures == 0 ? "모든 검사 통과" : "검사 실패: " + failures);
        return failures == 0 ? 0 : 1;
    }
}
