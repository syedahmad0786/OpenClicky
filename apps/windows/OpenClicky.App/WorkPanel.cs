using System.Diagnostics;
using OpenClicky.Core;
using OpenClicky.Providers;

namespace OpenClicky.App;

/// <summary>Named companions, their saved jobs, output files and schedules.</summary>
internal sealed class WorkPanel : UserControl
{
    private readonly WorkStore _store;
    private readonly Func<string, string?, Task<bool>> _run;
    private readonly ListBox _companions = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Companions" };
    private readonly ListBox _jobs = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Saved jobs" };
    private readonly ListBox _outputs = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Job output files" };
    private readonly ListView _routines = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, AccessibleName = "Job schedules" };
    private readonly Label _detail = AppTheme.Label("Choose or create a job.", 10);
    private readonly Label _status = AppTheme.Label("", 9);
    private IReadOnlyList<Companion> _companionItems = [];
    private IReadOnlyList<WorkJob> _jobItems = [];
    private IReadOnlyList<Routine> _routineItems = [];

    public WorkPanel(WorkStore store, Func<string, string?, Task<bool>> run)
    {
        _store = store;
        _run = run;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(12);
        _detail.MaximumSize = new Size(460, 0);
        _routines.Columns.Add("Schedule", 190); _routines.Columns.Add("Next run", 140); _routines.Columns.Add("Last result", 200);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        grid.Controls.Add(Column("Companions", _companions,
            Action("New", NewCompanion), Action("Edit", EditCompanion), Action("Notes", OpenNotes), Action("Archive", ArchiveCompanion)), 0, 0);
        grid.Controls.Add(Column("Jobs", _jobs,
            Action("New job", NewJob, primary: true), Action("Run now", () => RunSelected(null)), Action("Follow up", FollowUp), Action("Remove", RemoveJob)), 1, 0);

        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        foreach (var fill in new[] { false, false, true, false, false, true, false, false })
            detail.RowStyles.Add(fill ? new RowStyle(SizeType.Percent, 50) : new RowStyle(SizeType.AutoSize));
        detail.Controls.Add(_detail, 0, 0);
        detail.Controls.Add(AppTheme.Label("Files this job made", 10, true), 0, 1);
        detail.Controls.Add(_outputs, 0, 2);
        detail.Controls.Add(Buttons(Action("Open file", OpenOutput), Action("Open folder", OpenFolder)), 0, 3);
        detail.Controls.Add(AppTheme.Label("Schedule", 10, true), 0, 4);
        detail.Controls.Add(_routines, 0, 5);
        detail.Controls.Add(Buttons(Action("Add schedule", AddSchedule), Action("Pause / resume", TogglePause), Action("Delete schedule", DeleteSchedule)), 0, 6);
        detail.Controls.Add(_status, 0, 7);
        grid.Controls.Add(detail, 2, 0);

        var intro = AppTheme.Label("Each companion keeps its own notes and job folders. Jobs run in the background with your selected task access; schedules run while OpenClicky is open and still ask for approvals.", 9);
        intro.Dock = DockStyle.Top;
        Controls.Add(grid);
        Controls.Add(intro);
        _companions.SelectedIndexChanged += (_, _) => LoadJobs();
        _jobs.SelectedIndexChanged += (_, _) => ShowJob();
        _outputs.DoubleClick += (_, _) => OpenOutput();
    }

    public void RefreshWork()
    {
        var selectedCompanion = SelectedCompanion?.Slug;
        var selectedJob = SelectedJob?.Id;
        Try(() =>
        {
            _companionItems = _store.Companions();
            _companions.BeginUpdate(); _companions.Items.Clear();
            foreach (var companion in _companionItems) _companions.Items.Add(companion.Name);
            _companions.EndUpdate();
            if (_companionItems.Count > 0) _companions.SelectedIndex = Math.Max(0, _companionItems.ToList().FindIndex(c => c.Slug == selectedCompanion));
            LoadJobs();
            if (_jobItems.ToList().FindIndex(j => j.Id == selectedJob) is >= 0 and var jobIndex) _jobs.SelectedIndex = jobIndex;
        });
    }

    private Companion? SelectedCompanion => _companions.SelectedIndex >= 0 && _companions.SelectedIndex < _companionItems.Count ? _companionItems[_companions.SelectedIndex] : null;
    private WorkJob? SelectedJob => _jobs.SelectedIndex >= 0 && _jobs.SelectedIndex < _jobItems.Count ? _jobItems[_jobs.SelectedIndex] : null;
    private Routine? SelectedRoutine => _routines.SelectedIndices.Count == 1 ? _routineItems[_routines.SelectedIndices[0]] : null;

    private void LoadJobs()
    {
        if (SelectedCompanion is not { } companion) return;
        Try(() =>
        {
            _jobItems = _store.Jobs(companion.Slug);
            _jobs.BeginUpdate(); _jobs.Items.Clear();
            foreach (var job in _jobItems) _jobs.Items.Add($"{job.Title} · {job.Status}");
            _jobs.EndUpdate();
            if (_jobItems.Count > 0) _jobs.SelectedIndex = 0; else ShowJob();
        });
    }

    private void ShowJob()
    {
        _outputs.Items.Clear(); _routines.Items.Clear(); _routineItems = [];
        if (SelectedJob is not { } job)
        {
            _detail.Text = SelectedCompanion is { } c ? $"{c.Name}: {(c.Role.Length == 0 ? "General helper." : c.Role)}\nNo jobs yet. Choose New job." : "Choose or create a companion.";
            return;
        }
        Try(() =>
        {
            var last = job.LastRunAt is { } at ? at.ToLocalTime().ToString("g") : "never";
            _detail.Text = $"{job.Title} · {job.Status} · last run {last}\n{job.Prompt}" + (job.Summary.Length > 0 ? "\n\nLatest result: " + job.Summary : "");
            foreach (var file in _store.Outputs(job)) _outputs.Items.Add(file);
            _routineItems = _store.Routines(job.Id);
            foreach (var routine in _routineItems)
                _routines.Items.Add(new ListViewItem([
                    RoutineSchedule.Describe(routine) + (routine.IsPaused ? " (paused)" : ""),
                    routine.IsPaused ? "—" : routine.NextRunAt.ToLocalTime().ToString("g"),
                    routine.LastOutcome]));
        });
    }

    private void NewCompanion()
    {
        if (FieldsDialog.Ask(FindForm(), "New companion", [new("Name", ""), new("What is this companion for? (role)", "", Multiline: true)]) is not { } values) return;
        Try(() => { var companion = _store.CreateCompanion(values[0], values[1]); RefreshWork(); SelectCompanion(companion.Slug); }, "Companion created with its own notes and job folder.");
    }

    private void EditCompanion()
    {
        if (SelectedCompanion is not { } companion) return;
        if (FieldsDialog.Ask(FindForm(), "Edit companion", [new("Name", companion.Name), new("Role", companion.Role, Multiline: true)]) is not { } values) return;
        Try(() => { _store.UpdateCompanion(companion.Slug, values[0], values[1], false); RefreshWork(); }, "Companion updated.");
    }

    private void ArchiveCompanion()
    {
        if (SelectedCompanion is not { } companion) return;
        if (MessageBox.Show(FindForm(), $"Archive {companion.Name}? Its folder, notes and files stay on this PC.", "Archive companion",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Try(() => { _store.UpdateCompanion(companion.Slug, companion.Name, companion.Role, true); RefreshWork(); }, "Companion archived.");
    }

    private void OpenNotes()
    {
        if (SelectedCompanion is not { } companion) return;
        Try(() =>
        {
            _ = _store.ReadNotes(companion.Slug);
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_store.NotesPath(companion.Slug)}\"") { UseShellExecute = false });
        });
    }

    private void NewJob()
    {
        if (SelectedCompanion is not { } companion) return;
        if (FieldsDialog.Ask(FindForm(), $"New job for {companion.Name}", [new("Title", ""), new("What should it do?", "", Multiline: true)]) is not { } values) return;
        Try(() => { _store.CreateJob(companion.Slug, values[0], values[1]); LoadJobs(); }, "Job saved. Choose Run now or Add schedule.");
    }

    private void FollowUp()
    {
        if (SelectedJob is null) return;
        if (FieldsDialog.Ask(FindForm(), "Follow up", [new("What should change or continue?", "", Multiline: true)]) is { } values) RunSelected(values[0]);
    }

    private async void RunSelected(string? followUp)
    {
        if (SelectedJob is not { } job) return;
        var started = await _run(job.Id, followUp);
        ShowStatus(started ? "Job started. Follow its progress in Tasks." : "The job did not start; see Chat for the reason.");
        LoadJobs();
    }

    private void RemoveJob()
    {
        if (SelectedJob is not { } job) return;
        if (MessageBox.Show(FindForm(), $"Remove \"{job.Title}\" and its schedules from the list? Its folder and files stay on this PC.", "Remove job",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        Try(() => { _store.DeleteJob(job.Id); LoadJobs(); }, "Job removed from the list.");
    }

    private void OpenFolder()
    {
        if (SelectedJob is not { } job) return;
        Try(() => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.JobFolder(job)}\"") { UseShellExecute = false }));
    }

    private void OpenOutput()
    {
        if (SelectedJob is not { } job || _outputs.SelectedItem is not string relative) return;
        Try(() =>
        {
            var root = Path.GetFullPath(_store.OutputFolder(job));
            var path = Path.GetFullPath(Path.Combine(root, relative));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new FileNotFoundException("That file is no longer in the job's output folder.");
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        });
    }

    private void AddSchedule()
    {
        if (SelectedJob is not { } job) return;
        if (ScheduleDialog.Ask(FindForm()) is not { } choice) return;
        Try(() => { _store.AddRoutine(job.Id, choice.Cadence, choice.MinuteOfDay, choice.WeekdayMask, choice.IntervalMinutes, DateTimeOffset.Now); ShowJob(); },
            "Schedule added. It runs while OpenClicky is open; a missed run happens once when it returns.");
    }

    private void TogglePause()
    {
        if (SelectedRoutine is not { } routine) return;
        Try(() =>
        {
            var resuming = routine.IsPaused;
            _store.SaveRoutine(routine with
            {
                IsPaused = !resuming,
                NextRunAt = resuming ? RoutineSchedule.NextAfter(routine, DateTimeOffset.Now, TimeZoneInfo.Local) : routine.NextRunAt,
            });
            ShowJob();
        });
    }

    private void DeleteSchedule()
    {
        if (SelectedRoutine is not { } routine) return;
        Try(() => { _store.DeleteRoutine(routine.Id); ShowJob(); }, "Schedule deleted.");
    }

    private void SelectCompanion(string slug)
    {
        var index = _companionItems.ToList().FindIndex(c => c.Slug == slug);
        if (index >= 0) _companions.SelectedIndex = index;
    }

    private void Try(Action action, string? success = null)
    {
        try { action(); if (success is not null) ShowStatus(success); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowStatus(ex.Message, error: true); }
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : AppTheme.Muted;
    }

    private static Button Action(string text, Action click, bool primary = false)
    {
        var button = AppTheme.Button(text, primary);
        button.MinimumSize = new Size(80, 34);
        button.Click += (_, _) => click();
        return button;
    }

    private static FlowLayoutPanel Buttons(params Control[] buttons)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        flow.Controls.AddRange(buttons);
        return flow;
    }

    private static TableLayoutPanel Column(string title, Control list, params Control[] buttons)
    {
        var column = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 10, 0) };
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        column.Controls.Add(AppTheme.Label(title, 12, true), 0, 0);
        column.Controls.Add(list, 0, 1);
        column.Controls.Add(Buttons(buttons), 0, 2);
        return column;
    }
}

/// <summary>User-added MCP connectors for the Connections tab.</summary>
internal sealed class ConnectorsPanel : UserControl
{
    private readonly ConnectorRegistry _registry;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, AccessibleName = "Connected accounts" };
    private readonly Label _status = AppTheme.Label("", 9);
    private IReadOnlyList<ConnectorInfo> _items = [];

    public ConnectorsPanel(ConnectorRegistry registry)
    {
        _registry = registry;
        Size = new Size(640, 260);
        _list.Columns.Add("Connector", 200); _list.Columns.Add("Server", 260); _list.Columns.Add("Added", 140);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var add = AppTheme.Button("Add connector…", primary: true); add.Click += (_, _) => Add();
        var test = AppTheme.Button("Test"); test.Click += async (_, _) => await TestAsync();
        var remove = AppTheme.Button("Remove"); remove.Click += (_, _) => Remove();
        buttons.Controls.AddRange([add, test, remove, _status]);
        Controls.Add(_list);
        Controls.Add(buttons);
        RefreshConnectors();
    }

    public void RefreshConnectors()
    {
        try
        {
            _items = _registry.List();
            _list.BeginUpdate(); _list.Items.Clear();
            foreach (var item in _items) _list.Items.Add(new ListViewItem([item.DisplayName, item.Host, item.CreatedAt.ToLocalTime().ToString("d")]));
            _list.EndUpdate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowStatus(ex.Message, error: true); }
    }

    private ConnectorInfo? Selected => _list.SelectedIndices.Count == 1 ? _items[_list.SelectedIndices[0]] : null;

    private void Add()
    {
        if (FieldsDialog.Ask(FindForm(), "Add an MCP connector",
            [new("Name (for example: Work Gmail)", ""), new("MCP server address (https://…)", ""),
             new("Auth header name (optional)", "Authorization"), new("Auth header value (optional, stored in Windows Credential Manager)", "", Secret: true)]) is not { } values) return;
        try
        {
            var header = values[3].Length == 0 ? null : values[2];
            _registry.Add(values[0], values[1], header, values[3]);
            RefreshConnectors();
            ShowStatus("Added. Choose Test to check it before a task uses it.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ShowStatus(ex.Message, error: true); }
    }

    private async Task TestAsync()
    {
        if (Selected is not { } info) return;
        if (info.Kind != ConnectorKind.Remote || info.Auth == OpenClicky.Core.Settings.ConnectorAuth.Browser)
        {
            ShowStatus("Test this connector in Settings → Integrations.");
            return;
        }
        ShowStatus("Testing…");
        try
        {
            var secret = _registry.Secret(info.Key) ?? throw new InvalidOperationException("Its saved address is missing. Remove and add it again.");
            using var connector = new RemoteMcpConnector(info.Key, info.DisplayName, secret);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var tools = await connector.ListToolsAsync(timeout.Token);
            ShowStatus($"{info.DisplayName} answered with {tools.Count} tools ({tools.Count(t => t.ReadOnly)} marked read-only). Others need your approval in tasks.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowStatus($"{info.DisplayName} could not be reached: {ex.Message}", error: true); }
    }

    private void Remove()
    {
        if (Selected is not { } info) return;
        if (MessageBox.Show(FindForm(), $"Remove {info.DisplayName} and its saved credential from this PC? Revoke access at the provider too if you no longer use it.",
                "Remove connector", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { _registry.Remove(info.Key); RefreshConnectors(); ShowStatus("Removed."); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ShowStatus(ex.Message, error: true); }
    }

    private void ShowStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : AppTheme.Muted;
    }
}

internal sealed record DialogField(string Label, string Initial, bool Multiline = false, bool Secret = false);

/// <summary>A small modal form that returns one trimmed string per field, or null on Cancel.</summary>
internal static class FieldsDialog
{
    public static string[]? Ask(IWin32Window? owner, string title, IReadOnlyList<DialogField> fields)
    {
        using var form = NewDialog(title);
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        var boxes = fields.Select(field =>
        {
            flow.Controls.Add(AppTheme.Label(field.Label, 10));
            var box = new TextBox { Text = field.Initial, Width = 460, Multiline = field.Multiline, Height = field.Multiline ? 110 : 28,
                ScrollBars = field.Multiline ? ScrollBars.Vertical : ScrollBars.None, UseSystemPasswordChar = field.Secret, AccessibleName = field.Label,
                AcceptsReturn = field.Multiline };
            flow.Controls.Add(box);
            return box;
        }).ToArray();
        AddButtons(form, flow, "OK", acceptOnEnter: !fields.Any(f => f.Multiline));
        return form.ShowDialog(owner) == DialogResult.OK ? boxes.Select(b => b.Text.Trim()).ToArray() : null;
    }

    internal static Form NewDialog(string title)
    {
        var form = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16) };
        AppTheme.Apply(form);
        return form;
    }

    internal static void AddButtons(Form form, FlowLayoutPanel flow, string okText, bool acceptOnEnter)
    {
        var ok = AppTheme.Button(okText, primary: true); ok.DialogResult = DialogResult.OK;
        var cancel = AppTheme.Button("Cancel"); cancel.DialogResult = DialogResult.Cancel;
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.AddRange([ok, cancel]);
        flow.Controls.Add(buttons);
        form.Controls.Add(flow);
        form.AcceptButton = acceptOnEnter ? ok : null;
        form.CancelButton = cancel;
    }
}

internal sealed record ScheduleChoice(RoutineCadence Cadence, int MinuteOfDay, int WeekdayMask, int IntervalMinutes);

internal static class ScheduleDialog
{
    public static ScheduleChoice? Ask(IWin32Window? owner)
    {
        using var form = FieldsDialog.NewDialog("Add schedule");
        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, AccessibleName = "Repeat" };
        kind.Items.AddRange(["Every day", "On chosen weekdays", "Every few minutes or hours"]);
        kind.SelectedIndex = 0;
        var time = new DateTimePicker { Format = DateTimePickerFormat.Time, ShowUpDown = true, Value = DateTime.Today.AddHours(9), Width = 140, AccessibleName = "Time of day" };
        var days = new CheckedListBox { CheckOnClick = true, Height = 130, Width = 200, AccessibleName = "Weekdays" };
        foreach (var day in Enum.GetValues<DayOfWeek>()) days.Items.Add(day.ToString(), day is not (DayOfWeek.Saturday or DayOfWeek.Sunday));
        var minutes = new NumericUpDown { Minimum = RoutineSchedule.MinIntervalMinutes, Maximum = RoutineSchedule.MaxIntervalMinutes, Value = 60, Width = 120, AccessibleName = "Minutes between runs" };
        var minutesLabel = AppTheme.Label("Minutes between runs", 10);
        void Sync()
        {
            time.Enabled = kind.SelectedIndex != 2;
            days.Visible = kind.SelectedIndex == 1;
            minutes.Visible = minutesLabel.Visible = kind.SelectedIndex == 2;
        }
        kind.SelectedIndexChanged += (_, _) => Sync();
        flow.Controls.AddRange([AppTheme.Label("Repeat", 10), kind, AppTheme.Label("Time of day", 10), time, days, minutesLabel, minutes]);
        FieldsDialog.AddButtons(form, flow, "Add", acceptOnEnter: true);
        Sync();
        if (form.ShowDialog(owner) != DialogResult.OK) return null;
        var minuteOfDay = time.Value.Hour * 60 + time.Value.Minute;
        var mask = Enumerable.Range(0, 7).Where(days.GetItemChecked).Aggregate(0, (sum, day) => sum | 1 << day);
        return kind.SelectedIndex switch
        {
            0 => new(RoutineCadence.Daily, minuteOfDay, 0, 0),
            1 => new(RoutineCadence.Weekly, minuteOfDay, mask, 0),
            _ => new(RoutineCadence.Interval, 0, 0, (int)minutes.Value),
        };
    }
}
