using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class TaskWorkspacePanel : UserControl
{
    private readonly ListBox _runs = new() { Dock = DockStyle.Left, Width = 205, AccessibleName = "Saved tasks" };
    private readonly ListView _events = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, AccessibleName = "Task narration and progress" };
    private readonly Label _state = AppTheme.Label("No task yet. Ask your companion to do something.", 10, true);
    private IReadOnlyList<TaskRunRecord> _records = [];

    public TaskWorkspacePanel(Action pause, Func<Task> resume, Action stop, Action clear)
    {
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Padding = new Padding(12);
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 105, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        header.Controls.Add(AppTheme.Label("Look → Plan → Approve → Act → Observe", 12, true));
        header.Controls.Add(_state);
        header.Controls.Add(AppTheme.Label("Say pause task, resume task, stop task, or change task: followed by instructions. Inspect the destination result.", 8));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var pauseButton = AppTheme.Button("Pause task"); pauseButton.Click += (_, _) => pause();
        var resumeButton = AppTheme.Button("Resume task"); resumeButton.Click += async (_, _) => await resume();
        var stopButton = AppTheme.Button("Stop task"); stopButton.Click += (_, _) => stop();
        var export = AppTheme.Button("Export events"); export.Click += (_, _) => ExportSelected();
        var clearButton = AppTheme.Button("Clear tasks"); clearButton.Click += (_, _) =>
        {
            if (MessageBox.Show("Clear saved task text and progress? Your current task will be stopped.", "Clear task history", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes) clear();
        };
        actions.Controls.AddRange([pauseButton, resumeButton, stopButton, export, clearButton]);
        _events.Columns.Add("Step", 45); _events.Columns.Add("State", 115); _events.Columns.Add("What happened", 500);
        _runs.SelectedIndexChanged += (_, _) => ShowSelected();
        Controls.Add(_events); Controls.Add(_runs); Controls.Add(header); Controls.Add(actions);
    }

    public void UpdateRuns(IReadOnlyList<TaskRunRecord> records)
    {
        var selected = _runs.SelectedIndex >= 0 && _runs.SelectedIndex < _records.Count ? _records[_runs.SelectedIndex].Id : null;
        var nextSelected = TaskRunSelection.Resolve(_records, selected, records);
        _records = records.Reverse().ToArray();
        _runs.BeginUpdate(); _runs.Items.Clear();
        foreach (var run in _records) _runs.Items.Add($"{run.Status} · {run.Task}");
        _runs.EndUpdate();
        if (_records.Count > 0) _runs.SelectedIndex = Math.Max(0, Array.FindIndex(_records.ToArray(), r => r.Id == nextSelected));
        else { _events.Items.Clear(); _state.Text = "No task yet."; }
    }

    private void ShowSelected()
    {
        if (_runs.SelectedIndex < 0) return;
        var run = _records[_runs.SelectedIndex];
        _state.Text = $"{run.Status} · {run.AccessMode} · {run.Task}";
        _events.BeginUpdate(); _events.Items.Clear();
        foreach (var entry in run.Events)
        {
            var item = new ListViewItem([entry.Step.ToString(), entry.Status, entry.Message]) { ForeColor = entry.Status == "failed" ? Color.Firebrick : AppTheme.Ink };
            _events.Items.Add(item);
        }
        _events.EndUpdate();
        if (_events.Items.Count > 0) _events.EnsureVisible(_events.Items.Count - 1);
    }

    private void ExportSelected()
    {
        if (_runs.SelectedIndex < 0) return;
        using var dialog = new SaveFileDialog { Filter = "Task events (*.ndjson)|*.ndjson", FileName = "openclicky-task.ndjson" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, AgUiTaskEvents.Encode(_records[_runs.SelectedIndex])); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not export task"); }
    }
}

internal static class TaskRunSelection
{
    public static string? Resolve(
        IReadOnlyList<TaskRunRecord> previous,
        string? selectedId,
        IReadOnlyList<TaskRunRecord> current)
    {
        if (current.Count == 0) return null;

        var previousIds = previous.Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = current.Count - 1; index >= 0; index--)
        {
            if (!previousIds.Contains(current[index].Id)) return current[index].Id;
        }

        if (selectedId is not null && current.Any(run => run.Id == selectedId)) return selectedId;
        return current[^1].Id;
    }
}
