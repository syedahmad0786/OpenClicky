using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class DictationPanel : Panel
{
    private readonly DictationDraft _state = new();
    private readonly Label _target;
    private readonly Label _status;
    private readonly TextBox _raw;
    private readonly TextBox _draft;
    private readonly Button _insert;
    private readonly Button _record;
    private bool _syncing;
    private bool _sessionActive;
    private bool _inserting;
    private CompanionVoiceState _voiceState;

    public DictationPanel(Func<Task> toggleRecording, Func<string, Task> insert, Action cancel)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(20);
        AccessibleName = "Dictation preview";

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 8, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        Controls.Add(layout);

        layout.Controls.Add(AppTheme.Label("Dictation", 19, true));
        _target = AppTheme.Label("No destination selected", 9);
        layout.Controls.Add(_target);
        layout.Controls.Add(AppTheme.Label("Raw local transcript", 9, true));
        _raw = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Raw dictation transcript" };
        layout.Controls.Add(_raw);
        layout.Controls.Add(AppTheme.Label("Editable draft", 9, true));
        _draft = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 12000, AccessibleName = "Editable dictation draft" };
        _draft.TextChanged += (_, _) => { if (!_syncing) _state.Edit(_draft.Text); };
        layout.Controls.Add(_draft);
        _status = AppTheme.Label("Choose Start recording and speak. Audio and transcription stay on this PC.", 9);
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.AccessibleName = "Dictation status";
        layout.Controls.Add(_status);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        _insert = AppTheme.Button("Insert into original app", true);
        _record = AppTheme.Button("Start recording");
        var close = AppTheme.Button("Cancel");
        close.Click += (_, _) => cancel();
        _record.Click += async (_, _) => await toggleRecording();
        _insert.Click += async (_, _) =>
        {
            _inserting = true;
            RefreshActions();
            try { await insert(_draft.Text); }
            finally { _inserting = false; if (!IsDisposed) RefreshActions(); }
        };
        actions.Controls.Add(_insert);
        actions.Controls.Add(_record);
        actions.Controls.Add(close);
        layout.Controls.Add(actions);
        Hide();
    }

    public void Begin(string appTitle, string controlName, bool automatic = false)
    {
        _state.Reset();
        _sessionActive = true;
        _voiceState = CompanionVoiceState.Idle;
        _target.Text = $"Destination: {appTitle} · {controlName}";
        _status.Text = automatic
            ? "Listening locally. Pause to insert finalized words into the selected field. Changing focus stops insertion. Click the companion to stop."
            : "Review your local draft. Insert sends only text to the selected field; it never presses Send or Enter.";
        SetTexts("", "");
        RefreshActions();
        Show();
        BringToFront();
        if (!automatic) _draft.Focus();
    }

    public void ShowLive(string text)
    {
        if (!_sessionActive || string.IsNullOrWhiteSpace(text)) return;
        _state.AcceptTranscript(text);
        SetTexts(_state.RawTranscript, _state.Draft);
        _status.Text = "Live local preview · keep speaking, then pause or choose Stop recording.";
    }

    public void SetVoiceState(CompanionVoiceState state)
    {
        _voiceState = state;
        if (state == CompanionVoiceState.Listening) _status.Text = "Listening locally… first words can take a few seconds. Speak, then pause or choose Stop recording.";
        else if (state == CompanionVoiceState.Processing) _status.Text = "Finishing local transcription… audio stays on this PC.";
        RefreshActions();
    }

    private void RefreshActions()
    {
        var state = _voiceState;
        _record.Text = state == CompanionVoiceState.Listening ? "Stop recording"
            : state == CompanionVoiceState.Processing ? "Finishing…" : "Start recording";
        _record.AccessibleName = _record.Text;
        _record.Enabled = _sessionActive && !_inserting && state is CompanionVoiceState.Listening or CompanionVoiceState.Idle;
        _insert.Enabled = _sessionActive && !_inserting && state == CompanionVoiceState.Idle;
    }

    public void AcceptTranscript(string text)
    {
        _state.AcceptTranscript(text);
        SetTexts(_state.RawTranscript, _state.Draft);
        _status.Text = string.IsNullOrWhiteSpace(text)
            ? "No words were recognized. Check the selected microphone in Settings, then try Start recording again. Nothing was inserted."
            : "Review or edit the draft, then choose Insert. Cancel returns to screen questions. No AI request was made.";
    }

    public void SetResult(string message, bool canContinue = true)
    {
        _status.Text = message;
        _sessionActive = canContinue;
        RefreshActions();
    }

    public void End()
    {
        Hide();
        _sessionActive = false;
        _state.Reset();
    }

    private void SetTexts(string raw, string draft)
    {
        _syncing = true;
        try { _raw.Text = raw; _draft.Text = draft; }
        finally { _syncing = false; }
    }
}
