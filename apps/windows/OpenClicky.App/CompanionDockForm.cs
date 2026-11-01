using System.Drawing.Drawing2D;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>A non-activating entry point so starting voice keeps the target app in focus.</summary>
internal sealed class CompanionDockForm : Form
{
    private readonly Button _voice;
    private readonly Label _status;
    private readonly ToolTip _hint = new();
    private Point _dragStart;
    private Point _windowStart;
    private bool _dragging;
    private readonly System.Windows.Forms.Timer _follow = new() { Interval = 40 };
    private readonly Panel _offer = new() { Bounds = new Rectangle(4, 90, 280, 90), Visible = false };
    private Action? _acceptOffer, _snoozeOffer;
    private string? _taskState;
    private CompanionVoiceState _voiceState;
    private bool _conversationActive;

    public CompanionDockForm(Action toggleConversation, Action showHome, Action selectArea,
        Action showClipboard, Action stopConversation, Func<bool>? keepAnchored = null)
    {
        AppTheme.Apply(this);
        Text = "OpenClicky companion";
        AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(144, 90);
        BackColor = AppTheme.Background;

        _voice = new Button
        {
            Bounds = new Rectangle(44, 6, 56, 56), FlatStyle = FlatStyle.Flat,
            Image = new Bitmap(AppTheme.Logo, new Size(44, 44)), BackColor = Color.White,
            Cursor = Cursors.Hand, AccessibleName = "Start conversation", TabIndex = 0,
        };
        _voice.FlatAppearance.BorderColor = AppTheme.Border;
        using (var circle = new GraphicsPath())
        {
            circle.AddEllipse(0, 0, 55, 55);
            _voice.Region = new Region(circle);
        }
        _status = new Label
        {
            Bounds = new Rectangle(4, 65, 136, 21), Text = "Click to talk",
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = AppTheme.Ink,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open workspace", null, (_, _) => showHome());
        menu.Items.Add("Select screen area", null, (_, _) => selectArea());
        menu.Items.Add("Clipboard shelf", null, (_, _) => showClipboard());
        menu.Items.Add("Stop conversation", null, (_, _) => stopConversation());
        ContextMenuStrip = _voice.ContextMenuStrip = _status.ContextMenuStrip = menu;
        _hint.SetToolTip(_voice, "Click to start or stop conversation. Drag to move. Right-click for screen selection and your clipboard shelf.");
        _voice.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            _dragStart = Cursor.Position;
            _windowStart = Location;
            _dragging = false;
        };
        _voice.MouseMove += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var delta = new Point(Cursor.Position.X - _dragStart.X, Cursor.Position.Y - _dragStart.Y);
            if (!_dragging && Math.Abs(delta.X) + Math.Abs(delta.Y) < SystemInformation.DragSize.Width) return;
            _dragging = true;
            Location = new Point(_windowStart.X + delta.X, _windowStart.Y + delta.Y);
        };
        _voice.Click += (_, _) => { if (!_dragging) toggleConversation(); };
        Controls.AddRange([_voice, _status]);
        _offer.Controls.Add(new Label { Text = "Looking for something? I can help.", Bounds = new Rectangle(4, 0, 270, 28), AccessibleName = "Help offer" });
        var accept = new Button { Text = "Help me", Bounds = new Rectangle(4, 32, 82, 30) };
        var dismiss = new Button { Text = "Dismiss", Bounds = new Rectangle(90, 32, 82, 30) };
        var snooze = new Button { Text = "Snooze 30m", Bounds = new Rectangle(176, 32, 98, 30) };
        accept.Click += (_, _) => { var action = _acceptOffer; DismissOffer(); action?.Invoke(); };
        dismiss.Click += (_, _) => DismissOffer();
        snooze.Click += (_, _) => { var action = _snoozeOffer; DismissOffer(); action?.Invoke(); };
        _offer.Controls.AddRange([accept, dismiss, snooze]);
        Controls.Add(_offer);
        _follow.Tick += (_, _) =>
        {
            var cursor = Cursor.Position;
            var nearby = Bounds; nearby.Inflate(65, 65);
            if (_offer.Visible || _dragging || MouseButtons != MouseButtons.None || nearby.Contains(cursor) || keepAnchored?.Invoke() == true) return;
            var area = Screen.FromPoint(cursor).WorkingArea;
            Location = new Point(Math.Clamp(cursor.X + 32, area.Left, Math.Max(area.Left, area.Right - Width)),
                Math.Clamp(cursor.Y + 24, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        };
        _follow.Start();
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(work.Right - Width - 24, work.Bottom - Height - 24);
        ResumeLayout();
        UpdateShape();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_voice is not null && _status is not null) UpdateShape();
    }

    private void UpdateShape()
    {
        using var shape = new GraphicsPath();
        shape.AddEllipse(_voice.Bounds);
        shape.AddRectangle(_status.Bounds);
        if (_acceptOffer is not null) shape.AddRectangle(_offer.Bounds);
        var previous = Region;
        Region = new Region(shape);
        previous?.Dispose();
    }

    public void OfferHelp(Action accept, Action snooze)
    {
        _acceptOffer = accept; _snoozeOffer = snooze;
        ClientSize = new Size(288, 182);
        _offer.Show();
        UpdateShape();
        var area = Screen.FromPoint(Location).WorkingArea;
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    public void DismissOffer() { _offer.Hide(); _acceptOffer = null; _snoozeOffer = null; ClientSize = new Size(144, 90); UpdateShape(); }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x00000080; return p; }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }

    public void SetState(CompanionVoiceState state, bool conversationActive)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => SetState(state, conversationActive)); return; }
        _voiceState = state; _conversationActive = conversationActive;
        _status.Text = state switch
        {
            CompanionVoiceState.Listening => "Listening…",
            CompanionVoiceState.Processing => "Thinking…",
            CompanionVoiceState.Responding or CompanionVoiceState.Pointing => "Speaking…",
            _ => _taskState ?? (conversationActive ? "Conversation on" : "Click to talk"),
        };
        _voice.AccessibleName = conversationActive ? "Stop conversation" : "Start conversation";
        _voice.BackColor = conversationActive ? AppTheme.Tint : Color.White;
    }

    public void SetTaskState(string state)
    {
        _taskState = state switch { "paused" => "Paused", "failed" => "Needs attention", "completed" or "stopped" => null, _ => "Working…" };
        SetState(_voiceState, _conversationActive);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _follow.Dispose(); _voice.Image?.Dispose(); _hint.Dispose(); ContextMenuStrip?.Dispose(); }
        base.Dispose(disposing);
    }
}
