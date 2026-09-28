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
    private static readonly Color Background = Color.White;
    private static readonly Color Card = Color.FromArgb(249, 249, 247);
    private static readonly Color Border = Color.FromArgb(225, 225, 221);
    private static readonly Color Muted = Color.FromArgb(105, 108, 112);
    private static readonly Color Accent = Color.FromArgb(68, 124, 216);
    private static readonly Color Ink = Color.FromArgb(34, 35, 36);

    private readonly SessionStore store = new SessionStore(SessionStore.DefaultRoot());
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly ToolTip titleTip = new ToolTip();
    private readonly Label heading = new Label();
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
    private readonly Label refreshed = new Label();
    private SessionOverview overview;
    private bool loading;
    private string loadError;
    private string selectedTaskId;

    public ClockForm()
    {
        Text = "코덱스 시계";
        var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null) Icon = appIcon;
        BackColor = Background;
        ForeColor = Ink;
        Font = new Font("맑은 고딕", 8f);
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(340, 380);
        MinimumSize = new Size(330, 390);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        DoubleBuffered = true;

        Style(heading, 10f, FontStyle.Bold, Ink);
        heading.Text = "코덱스 작업 시계";

        refreshButton.Text = "새로고침";
        refreshButton.Font = new Font("맑은 고딕", 8f);
        refreshButton.FlatStyle = FlatStyle.Flat;
        refreshButton.FlatAppearance.BorderColor = Border;
        refreshButton.FlatAppearance.MouseOverBackColor = Card;
        refreshButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(241, 241, 239);
        refreshButton.BackColor = Background;
        refreshButton.ForeColor = Ink;
        refreshButton.Cursor = Cursors.Hand;
        refreshButton.Click += (sender, args) => RefreshData();

        topCheck.Text = "항상 위";
        topCheck.ForeColor = Muted;
        topCheck.CheckedChanged += (sender, args) => TopMost = topCheck.Checked;
        privacyCheck.Text = "제목 숨김";
        privacyCheck.ForeColor = Muted;
        privacyCheck.CheckedChanged += (sender, args) => Render();

        mainCard.BackColor = Card;
        mainCard.Paint += (sender, args) => {
            using (var pen = new Pen(Border))
                args.Graphics.DrawRectangle(pen, 0, 0, mainCard.ClientSize.Width - 1, mainCard.ClientSize.Height - 1);
        };
        Style(status, 7.5f, FontStyle.Bold, Accent);
        Style(taskTitle, 9.5f, FontStyle.Bold, Ink);
        taskTitle.AutoEllipsis = true;
        Style(elapsedTitle, 7f, FontStyle.Regular, Muted);
        elapsedTitle.Text = "경과";
        Style(elapsedValue, 18f, FontStyle.Bold, Ink);
        Style(etaTitle, 7f, FontStyle.Regular, Muted);
        etaTitle.Text = "남은 시간 · 추정";
        Style(etaValue, 9f, FontStyle.Regular, Ink);
        etaValue.AutoEllipsis = true;
        Style(activity, 7f, FontStyle.Regular, Muted);
        activity.AutoEllipsis = true;
        mainCard.Controls.AddRange(new Control[] {
            status, taskTitle, elapsedTitle, elapsedValue, etaTitle, etaValue, activity
        });

        Style(listHeading, 8f, FontStyle.Bold, Ink);
        listHeading.Text = "최근 작업";
        recentList.FlowDirection = FlowDirection.TopDown;
        recentList.WrapContents = false;
        recentList.AutoScroll = true;
        recentList.BackColor = Background;
        recentList.Padding = new Padding(0);
        Style(refreshed, 7f, FontStyle.Regular, Muted);

        Controls.AddRange(new Control[] {
            heading, refreshButton, topCheck, privacyCheck, mainCard,
            listHeading, recentList, refreshed
        });
        FormClosed += (sender, args) => titleTip.Dispose();
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
        var width = ClientSize.Width - 28;
        var refreshWidth = Math.Max(88, TextRenderer.MeasureText(refreshButton.Text, refreshButton.Font).Width + 24);
        var refreshLeft = ClientSize.Width - refreshWidth - 28;
        heading.SetBounds(16, 9, refreshLeft - 24, 25);
        refreshButton.SetBounds(refreshLeft, 10, refreshWidth, 25);
        privacyCheck.SetBounds(16, 38, 95, 19);
        topCheck.SetBounds(120, 38, 80, 19);
        mainCard.SetBounds(14, 63, width, 177);
        status.SetBounds(12, 7, width - 24, 18);
        taskTitle.SetBounds(12, 27, width - 24, 23);
        elapsedTitle.SetBounds(12, 54, width - 24, 16);
        elapsedValue.SetBounds(12, 70, width - 24, 42);
        etaTitle.SetBounds(12, 114, width - 24, 16);
        etaValue.SetBounds(12, 131, width - 24, 23);
        activity.SetBounds(12, 155, width - 24, 17);
        listHeading.SetBounds(14, 247, width, 18);
        recentList.SetBounds(14, 269, width, Math.Max(0, ClientSize.Height - 295));
        refreshed.SetBounds(14, ClientSize.Height - 21, width, 16);
        foreach (Control row in recentList.Controls) row.Width = recentList.ClientSize.Width - 18;
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
        titleTip.SetToolTip(taskTitle, "");
        if (loadError != null)
        {
            status.Text = "읽기 실패";
            taskTitle.Text = "새로고침을 눌러 주세요";
            elapsedValue.Text = "—";
            etaValue.Text = "확인할 수 없어요";
            activity.Text = loadError;
            return;
        }
        if (overview == null)
        {
            status.Text = "확인 중";
            taskTitle.Text = "기록 읽는 중";
            elapsedValue.Text = "—";
            etaValue.Text = "계산 중";
            activity.Text = "";
            return;
        }
        if (overview.Recent.Count == 0)
        {
            status.Text = "기록 없음";
            taskTitle.Text = "코덱스 작업이 없어요";
            elapsedValue.Text = "—";
            etaValue.Text = "비교 기록 부족";
            activity.Text = "로컬 기록 확인 중";
            recentList.Controls.Clear();
            refreshed.Text = DateTime.Now.ToString("HH:mm") + " 갱신";
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
        status.ForeColor = primary.IsActive ? Accent : Muted;
        taskTitle.Text = privacyCheck.Checked ? "제목 숨김" : primary.Title;
        titleTip.SetToolTip(taskTitle, taskTitle.Text);
        var elapsed = primary.IsActive && primary.StartedAtUtc.HasValue
            ? now - primary.StartedAtUtc.Value
            : primary.CompletedDurations.LastOrDefault();
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        elapsedTitle.Text = primary.IsActive ? "경과" : "소요";
        elapsedValue.Text = FormatDuration(elapsed);
        etaTitle.Text = "남은 시간 · 추정";
        if (primary.IsActive)
        {
            var estimate = TimeEstimate.FromHistory(elapsed, overview.HistoricalDurations);
            if (estimate.IsConditional) etaTitle.Text = "오래 걸린 기록 기준 · 추정";
            etaValue.Text = estimate.HasEstimate
                ? "약 " + FormatMinutes(estimate.Remaining)
                : estimate.ExceededTypicalRange
                    ? "평소보다 길어 예측 어려움" : "비교 기록 부족";
        }
        else etaValue.Text = "완료";
        activity.Text = "최근: " + primary.LastActivity;
        RenderRecent(primary);
        refreshed.Text = DateTime.Now.ToString("HH:mm") + " 갱신";
    }

    private void RenderRecent(TaskSnapshot primary)
    {
        recentList.SuspendLayout();
        foreach (Control oldRow in recentList.Controls.Cast<Control>().ToArray()) oldRow.Dispose();
        foreach (var task in overview.Recent.Where(x => x != primary).Take(4))
        {
            var row = new Panel { BackColor = Card, Height = 37,
                Width = recentList.ClientSize.Width - 18, Margin = new Padding(0, 0, 0, 4) };
            var name = new Label { Text = privacyCheck.Checked ? "제목 숨김" : task.Title, AutoEllipsis = true,
                Font = new Font("맑은 고딕", 8f, FontStyle.Bold), ForeColor = Ink,
                BackColor = Color.Transparent, Location = new Point(10, 2),
                Size = new Size(row.Width - 20, 18), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            var duration = task.IsActive && task.StartedAtUtc.HasValue
                ? DateTime.UtcNow - task.StartedAtUtc.Value : task.CompletedDurations.LastOrDefault();
            var detail = new Label { Text = (task.IsActive ? "진행 중" : "완료") + " · " + FormatDuration(duration),
                Font = new Font("맑은 고딕", 7f), ForeColor = task.IsActive ? Accent : Muted,
                BackColor = Color.Transparent, Location = new Point(10, 19),
                Size = new Size(row.Width - 20, 15), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            row.Controls.Add(name);
            row.Controls.Add(detail);
            titleTip.SetToolTip(name, name.Text);
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

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.W))
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
