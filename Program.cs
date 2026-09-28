using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ClockForm());
    }
}

internal sealed class ClockForm : Form
{
    private static readonly Color Background = Color.FromArgb(14, 20, 34);
    private static readonly Color Card = Color.FromArgb(25, 35, 55);
    private static readonly Color Muted = Color.FromArgb(157, 172, 195);
    private static readonly Color Accent = Color.FromArgb(91, 211, 187);
    private static readonly Color White = Color.FromArgb(242, 248, 255);

    private readonly SessionStore store = new SessionStore(SessionStore.DefaultRoot());
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Label heading = new Label();
    private readonly Label caption = new Label();
    private readonly Button refreshButton = new Button();
    private readonly CheckBox topCheck = new CheckBox();
    private readonly CheckBox privacyCheck = new CheckBox();
    private readonly Panel mainCard = new Panel();
    private readonly Label status = new Label();
    private readonly Label taskTitle = new Label();
    private readonly Label elapsedTitle = new Label();
    private readonly Label elapsedValue = new Label();
    private readonly Label etaTitle = new Label();
    private readonly Label etaValue = new Label();
    private readonly Label activity = new Label();
    private readonly Label listHeading = new Label();
    private readonly FlowLayoutPanel recentList = new FlowLayoutPanel();
    private readonly Label footnote = new Label();
    private readonly Label refreshed = new Label();
    private SessionOverview overview;
    private bool loading;
    private string loadError;
    private string selectedTaskId;

    public ClockForm()
    {
        Text = "코덱스 작업 시계";
        BackColor = Background;
        ForeColor = White;
        Font = new Font("맑은 고딕", 10f);
        ClientSize = new Size(472, 574);
        MinimumSize = new Size(440, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        DoubleBuffered = true;

        Style(heading, 19f, FontStyle.Bold, White);
        heading.Text = "코덱스 작업 시계";
        Style(caption, 9f, FontStyle.Regular, Muted);
        caption.Text = "내 컴퓨터의 작업 기록을 확인해요";

        refreshButton.Text = "새로고침";
        refreshButton.FlatStyle = FlatStyle.Flat;
        refreshButton.FlatAppearance.BorderColor = Color.FromArgb(70, 88, 111);
        refreshButton.BackColor = Color.FromArgb(33, 46, 68);
        refreshButton.ForeColor = White;
        refreshButton.Cursor = Cursors.Hand;
        refreshButton.Click += (sender, args) => RefreshData();

        topCheck.Text = "항상 위";
        topCheck.ForeColor = Muted;
        topCheck.CheckedChanged += (sender, args) => TopMost = topCheck.Checked;
        privacyCheck.Text = "제목 숨김";
        privacyCheck.ForeColor = Muted;
        privacyCheck.CheckedChanged += (sender, args) => Render();

        mainCard.BackColor = Card;
        Style(status, 10f, FontStyle.Bold, Accent);
        Style(taskTitle, 13f, FontStyle.Bold, White);
        taskTitle.AutoEllipsis = true;
        Style(elapsedTitle, 9f, FontStyle.Regular, Muted);
        elapsedTitle.Text = "지금까지 걸린 시간";
        Style(elapsedValue, 29f, FontStyle.Bold, White);
        Style(etaTitle, 9f, FontStyle.Regular, Muted);
        etaTitle.Text = "남은 시간 · 참고용";
        Style(etaValue, 12f, FontStyle.Bold, Accent);
        Style(activity, 9f, FontStyle.Regular, Muted);
        activity.AutoEllipsis = true;
        mainCard.Controls.AddRange(new Control[] {
            status, taskTitle, elapsedTitle, elapsedValue, etaTitle, etaValue, activity
        });

        Style(listHeading, 11f, FontStyle.Bold, White);
        listHeading.Text = "최근 작업 · 누르면 자세히 보기";
        recentList.FlowDirection = FlowDirection.TopDown;
        recentList.WrapContents = false;
        recentList.AutoScroll = true;
        recentList.BackColor = Background;
        recentList.Padding = new Padding(0);
        Style(footnote, 8.5f, FontStyle.Regular, Muted);
        footnote.Text = "남은 시간은 지난 완료 작업을 바탕으로 한 참고치예요.";
        Style(refreshed, 8.5f, FontStyle.Regular, Muted);

        Controls.AddRange(new Control[] {
            heading, caption, refreshButton, topCheck, privacyCheck, mainCard,
            listHeading, recentList, footnote, refreshed
        });
        Resize += (sender, args) => Arrange();
        Arrange();

        timer.Interval = 5000;
        timer.Tick += (sender, args) => { Render(); RefreshData(); };
        timer.Start();
        Render();
        RefreshData();
    }

    private static void Style(Label label, float size, FontStyle fontStyle, Color color)
    {
        label.Font = new Font("맑은 고딕", size, fontStyle);
        label.ForeColor = color;
        label.BackColor = Color.Transparent;
        label.TextAlign = ContentAlignment.MiddleLeft;
    }

    private void Arrange()
    {
        var width = ClientSize.Width - 40;
        heading.SetBounds(20, 17, width - 95, 31);
        caption.SetBounds(20, 49, width - 95, 21);
        refreshButton.SetBounds(ClientSize.Width - 108, 20, 88, 33);
        topCheck.SetBounds(ClientSize.Width - 102, 57, 83, 25);
        privacyCheck.SetBounds(ClientSize.Width - 213, 57, 100, 25);
        mainCard.SetBounds(20, 92, width, 250);
        status.SetBounds(18, 13, width - 36, 25);
        taskTitle.SetBounds(18, 41, width - 36, 34);
        elapsedTitle.SetBounds(18, 82, width - 36, 24);
        elapsedValue.SetBounds(18, 106, width - 36, 55);
        etaTitle.SetBounds(18, 165, width - 36, 21);
        etaValue.SetBounds(18, 187, width - 36, 29);
        activity.SetBounds(18, 219, width - 36, 23);
        listHeading.SetBounds(20, 351, width, 25);
        recentList.SetBounds(20, 380, width, Math.Max(90, ClientSize.Height - 447));
        footnote.SetBounds(20, ClientSize.Height - 61, width, 22);
        refreshed.SetBounds(20, ClientSize.Height - 38, width, 20);
        foreach (Control row in recentList.Controls) row.Width = recentList.ClientSize.Width - 20;
    }

    private void RefreshData()
    {
        if (loading) return;
        loading = true;
        ThreadPool.QueueUserWorkItem(delegate {
            SessionOverview loaded = null;
            string error = null;
            try { loaded = store.Load(DateTime.UtcNow); }
            catch (Exception) { error = "기록을 읽는 중 문제가 생겼어요"; }
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate {
                    overview = loaded;
                    loadError = error;
                    loading = false;
                    Render();
                });
            }
            catch (InvalidOperationException) { }
        });
    }

    private void Render()
    {
        if (loadError != null)
        {
            status.Text = "기록을 읽지 못했어요";
            taskTitle.Text = "새로고침을 눌러 다시 시도하세요";
            elapsedValue.Text = "—";
            etaValue.Text = "확인할 수 없어요";
            activity.Text = loadError;
            return;
        }
        if (overview == null)
        {
            status.Text = "기록 읽는 중";
            taskTitle.Text = "잠시만 기다려 주세요";
            elapsedValue.Text = "—";
            etaValue.Text = "계산 중";
            activity.Text = "";
            return;
        }
        if (overview.Recent.Count == 0)
        {
            status.Text = "표시할 기록이 없어요";
            taskTitle.Text = "코덱스에서 작업을 시작해 주세요";
            elapsedValue.Text = "—";
            etaValue.Text = "기록이 쌓이면 표시돼요";
            activity.Text = "로컬 세션 파일을 찾고 있어요";
            recentList.Controls.Clear();
            refreshed.Text = "마지막 확인: " + DateTime.Now.ToString("HH:mm:ss");
            return;
        }

        var now = DateTime.UtcNow;
        var primary = selectedTaskId == null ? null
            : overview.Recent.FirstOrDefault(x => x.Id == selectedTaskId);
        if (primary == null)
        {
            selectedTaskId = null;
            primary = overview.Recent.FirstOrDefault(x => x.IsActive) ?? overview.Recent[0];
        }
        var inactive = now - primary.LastActivityUtc;
        status.Text = primary.IsActive
            ? (inactive >= TimeSpan.FromMinutes(10) ? "진행 상태 확인 필요" : "진행 중")
            : "완료";
        status.ForeColor = primary.IsActive ? Accent : Color.FromArgb(123, 171, 232);
        taskTitle.Text = privacyCheck.Checked ? "제목 숨김" : primary.Title;
        var elapsed = primary.IsActive && primary.StartedAtUtc.HasValue
            ? now - primary.StartedAtUtc.Value
            : primary.CompletedDurations.LastOrDefault();
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        elapsedTitle.Text = primary.IsActive ? "지금까지 걸린 시간" : "마지막 작업에 걸린 시간";
        elapsedValue.Text = FormatDuration(elapsed);
        etaTitle.Text = "남은 시간 · 참고용";
        if (primary.IsActive)
        {
            var estimate = TimeEstimate.FromHistory(elapsed, overview.HistoricalDurations);
            if (estimate.IsConditional) etaTitle.Text = "비슷하게 오래 걸린 작업 기준 · 참고용";
            etaValue.Text = estimate.HasRange
                ? "약 " + FormatMinutes(estimate.MinRemaining) + " ~ " + FormatMinutes(estimate.MaxRemaining)
                : estimate.ExceededTypicalRange
                    ? "평소 범위 초과 · 예측 어려움" : "비슷한 완료 기록이 부족해요";
        }
        else etaValue.Text = "완료된 작업이에요";
        activity.Text = "최근 활동: " + primary.LastActivity;
        RenderRecent(primary);
        refreshed.Text = "마지막 확인: " + DateTime.Now.ToString("HH:mm:ss") + " · 내 컴퓨터에서만 확인";
    }

    private void RenderRecent(TaskSnapshot primary)
    {
        recentList.SuspendLayout();
        recentList.Controls.Clear();
        foreach (var task in overview.Recent.Where(x => x != primary).Take(4))
        {
            var row = new Panel { BackColor = Card, Height = 49,
                Width = recentList.ClientSize.Width - 20, Margin = new Padding(0, 0, 0, 7) };
            var name = new Label { Text = privacyCheck.Checked ? "제목 숨김" : task.Title, AutoEllipsis = true,
                Font = new Font("맑은 고딕", 9.5f, FontStyle.Bold), ForeColor = White,
                BackColor = Color.Transparent, Location = new Point(11, 5),
                Size = new Size(row.Width - 22, 22) };
            var duration = task.IsActive && task.StartedAtUtc.HasValue
                ? DateTime.UtcNow - task.StartedAtUtc.Value : task.CompletedDurations.LastOrDefault();
            var detail = new Label { Text = (task.IsActive ? "진행 중" : "완료") + " · " + FormatDuration(duration),
                Font = new Font("맑은 고딕", 8.5f), ForeColor = task.IsActive ? Accent : Muted,
                BackColor = Color.Transparent, Location = new Point(11, 27),
                Size = new Size(row.Width - 22, 17) };
            row.Controls.Add(name);
            row.Controls.Add(detail);
            row.Cursor = Cursors.Hand;
            name.Cursor = Cursors.Hand;
            detail.Cursor = Cursors.Hand;
            var id = task.Id;
            EventHandler choose = (sender, args) => { selectedTaskId = id; Render(); };
            row.Click += choose;
            name.Click += choose;
            detail.Click += choose;
            recentList.Controls.Add(row);
        }
        recentList.ResumeLayout();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.FromMinutes(1)) return Math.Max(0, (int)duration.TotalSeconds) + "초";
        if (duration < TimeSpan.FromHours(1)) return (int)duration.TotalMinutes + "분 " + duration.Seconds + "초";
        return (int)duration.TotalHours + "시간 " + duration.Minutes + "분";
    }

    private static string FormatMinutes(TimeSpan duration)
    {
        return Math.Ceiling(Math.Max(0, duration.TotalMinutes)).ToString("0") + "분";
    }
}
