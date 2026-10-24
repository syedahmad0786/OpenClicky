using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class CompanionMemoryPanel : UserControl
{
    private readonly Action? _changed;
    private readonly TextBox _name;
    private readonly TextBox _personality;
    private readonly TextBox _instructions;
    private readonly ListBox _memories;
    private readonly TextBox _memoryText;
    private readonly LinkLabel _memorySource;
    private readonly Button _saveProfile;
    private readonly Button _addMemory;
    private readonly Button _updateMemory;
    private readonly Button _deleteMemory;
    private readonly Button _export;
    private readonly Button _clear;
    private readonly Label _memoryCount;
    private readonly Label _status;
    private readonly Label _error;
    private bool _canEdit;

    public CompanionMemoryPanel(Action? changed = null)
    {
        _changed = changed;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Ink;
        Font = new Font("Segoe UI", 10f);
        AccessibleName = "Companion memory";
        AccessibleDescription = "Edit only the details you explicitly want OpenClicky to remember.";

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = AppTheme.Background };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(24, 20, 24, 24),
            BackColor = AppTheme.Background,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        content.Controls.Add(Heading("What your companion remembers", 19), 0, content.RowCount++);
        var privacy = AppTheme.Label(
            "Memory is stored locally on this PC and included in future AI requests. " +
            "OpenClicky can also learn a small set of facts you state explicitly, with a link to the local conversation source.", 9);
        privacy.Dock = DockStyle.Fill;
        privacy.MaximumSize = new Size(760, 0);
        content.Controls.Add(privacy, 0, content.RowCount++);

        _error = AppTheme.Label(string.Empty, 9, true);
        _error.ForeColor = Color.FromArgb(176, 35, 35);
        _error.Dock = DockStyle.Fill;
        _error.MaximumSize = new Size(760, 0);
        _error.Visible = false;
        _error.AccessibleName = "Companion memory error";
        content.Controls.Add(_error, 0, content.RowCount++);

        var profile = Section();
        profile.Controls.Add(Heading("Companion profile", 13), 0, profile.RowCount++);
        _name = AddTextBox(profile, "Name", CompanionMemoryStore.MaxNameLength, 30);
        _personality = AddTextBox(profile, "Personality", CompanionMemoryStore.MaxPersonalityLength, 72, multiline: true);
        _instructions = AddTextBox(profile, "Instructions", CompanionMemoryStore.MaxInstructionsLength, 96, multiline: true);
        _saveProfile = AppTheme.Button("Save profile", true);
        _saveProfile.Click += (_, _) => SaveProfile();
        profile.Controls.Add(ButtonRow(_saveProfile), 0, profile.RowCount++);
        content.Controls.Add(profile, 0, content.RowCount++);

        var facts = Section();
        facts.Controls.Add(Heading("Memories", 13), 0, facts.RowCount++);
        _memoryCount = AppTheme.Label($"0 of {CompanionMemoryStore.MaxFacts} memories", 8);
        _memoryCount.AccessibleName = "Memory count";
        facts.Controls.Add(_memoryCount, 0, facts.RowCount++);
        _memories = new ListBox
        {
            Dock = DockStyle.Fill,
            Height = 180,
            IntegralHeight = false,
            DisplayMember = nameof(CompanionMemoryItem.Text),
            FormattingEnabled = true,
            AccessibleName = "Saved manual and learned memories",
            BackColor = Color.White,
            ForeColor = AppTheme.Ink,
        };
        _memories.Format += (_, e) =>
        {
            if (e.ListItem is CompanionMemoryItem item)
                e.Value = $"[{(item.SourceKind is null ? "Manual" : "Learned")}] {item.Text}";
        };
        facts.Controls.Add(_memories, 0, facts.RowCount++);
        _memorySource = new LinkLabel
        {
            AutoSize = true,
            Visible = false,
            AccessibleName = "Learned memory source",
            LinkColor = AppTheme.Accent,
            Margin = new Padding(0, 4, 0, 8),
        };
        _memorySource.LinkClicked += (_, _) => ShowSelectedSource();
        facts.Controls.Add(_memorySource, 0, facts.RowCount++);
        _memoryText = AddTextBox(
            facts,
            "Memory to add or update",
            CompanionMemoryStore.MaxFactLength,
            72,
            multiline: true);
        _memories.SelectedIndexChanged += (_, _) =>
        {
            if (_memories.SelectedItem is CompanionMemoryItem item)
            {
                _memoryText.Text = item.Text;
                ShowSource(item);
            }
            else _memorySource.Visible = false;
            RefreshButtons();
        };
        _addMemory = AppTheme.Button("Add memory", true);
        _addMemory.Click += (_, _) => AddMemory();
        _updateMemory = AppTheme.Button("Update selected");
        _updateMemory.Click += (_, _) => UpdateMemory();
        _deleteMemory = AppTheme.Button("Delete selected");
        _deleteMemory.Click += (_, _) => DeleteMemory();
        facts.Controls.Add(ButtonRow(_addMemory, _updateMemory, _deleteMemory), 0, facts.RowCount++);
        content.Controls.Add(facts, 0, content.RowCount++);

        var data = Section();
        data.Controls.Add(Heading("Local memory file", 13), 0, data.RowCount++);
        _status = AppTheme.Label("Loading companion memory…", 9);
        _status.Dock = DockStyle.Fill;
        _status.MaximumSize = new Size(760, 0);
        _status.AccessibleName = "Companion memory status";
        data.Controls.Add(_status, 0, data.RowCount++);
        _export = AppTheme.Button("Export JSON");
        _export.Click += (_, _) => Export();
        _clear = AppTheme.Button("Clear all memory");
        _clear.Click += (_, _) => Clear();
        data.Controls.Add(ButtonRow(_export, _clear), 0, data.RowCount++);
        content.Controls.Add(data, 0, content.RowCount++);

        scroll.Controls.Add(content);
        Controls.Add(scroll);
        RefreshMemory();
    }

    public void RefreshMemory()
    {
        if (IsDisposed)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(RefreshMemory);
            return;
        }

        try
        {
            ApplyLoadResult(CompanionMemoryStore.Load());
        }
        catch (Exception ex)
        {
            ApplyUnreadable("Companion memory could not be accessed and was left unchanged: " + ex.Message);
        }
    }

    private void ApplyLoadResult(CompanionMemoryLoadResult result)
    {
        if (!result.CanEdit || result.Memory is null)
        {
            ApplyUnreadable(result.Error ?? "The saved companion memory could not be read and was left unchanged.");
            return;
        }

        _canEdit = true;
        _error.Visible = false;
        _name.Text = result.Memory.Name;
        _personality.Text = result.Memory.Personality;
        _instructions.Text = result.Memory.Instructions;
        FillMemories(result.Memory.Facts);
        SetEditorsReadOnly(false);
        _export.Enabled = result.Status == CompanionMemoryLoadStatus.Loaded;
        _clear.Enabled = result.Status == CompanionMemoryLoadStatus.Loaded;
        _status.Text = result.Status == CompanionMemoryLoadStatus.Missing
            ? "Nothing is saved yet. Add only details you want used in future requests."
            : $"Saved locally · {result.Memory.Facts.Count(f => f.SourceKind is null)} manual · {result.Memory.Facts.Count(f => f.SourceKind is not null)} learned.";
        RefreshButtons();
    }

    private void ApplyUnreadable(string message)
    {
        _canEdit = false;
        _name.Clear();
        _personality.Clear();
        _instructions.Clear();
        _memoryText.Clear();
        _memorySource.Visible = false;
        FillMemories([]);
        SetEditorsReadOnly(true);
        _error.Text = message;
        _error.Visible = true;
        _status.Text = "Editing is locked so the existing file cannot be overwritten. Export it or clear it to recover.";
        _export.Enabled = true;
        _clear.Enabled = true;
        RefreshButtons();
    }

    private void FillMemories(IReadOnlyList<CompanionMemoryItem> facts, string? selectedId = null)
    {
        _memories.BeginUpdate();
        try
        {
            _memories.Items.Clear();
            foreach (var fact in facts)
            {
                _memories.Items.Add(fact);
                if (fact.Id == selectedId)
                {
                    _memories.SelectedItem = fact;
                }
            }
        }
        finally
        {
            _memories.EndUpdate();
        }
        _memoryCount.Text = $"{facts.Count} of {CompanionMemoryStore.MaxFacts} memories";
    }

    private void ShowSource(CompanionMemoryItem item)
    {
        _memorySource.Visible = false;
        if (item.SourceKind != "conversation" || !long.TryParse(item.SourceId, out var id)) return;
        ConversationTurn? turn;
        try { turn = ConversationHistoryStore.Find(id); }
        catch { turn = null; }
        _memorySource.Text = turn is null
            ? $"Learned from deleted conversation #{id}"
            : $"Learned from conversation #{id}: {Shorten(turn.User, 140)}";
        _memorySource.Links.Clear();
        if (turn is not null) _memorySource.Links.Add(0, _memorySource.Text.Length, turn);
        _memorySource.Visible = true;
    }

    private void ShowSelectedSource()
    {
        if (_memorySource.Links.Count == 0 || _memorySource.Links[0].LinkData is not ConversationTurn turn) return;
        MessageBox.Show(turn.User, $"Conversation #{turn.Id} source", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string Shorten(string text, int maximum) =>
        text.Length <= maximum ? text : text[..maximum].TrimEnd() + "…";

    private void SaveProfile() => Mutate(
        () => CompanionMemoryStore.SetProfile(_name.Text, _personality.Text, _instructions.Text),
        "Companion profile saved.");

    private void AddMemory()
    {
        Mutate(() => CompanionMemoryStore.AddFact(_memoryText.Text), "Memory added.");
        if (_canEdit && !_error.Visible)
        {
            _memoryText.Clear();
        }
    }

    private void UpdateMemory()
    {
        if (_memories.SelectedItem is not CompanionMemoryItem selected)
        {
            ShowError("Select a memory to update.");
            return;
        }

        Mutate(
            () =>
            {
                if (!CompanionMemoryStore.UpdateFact(selected.Id, _memoryText.Text))
                {
                    throw new InvalidOperationException("That memory no longer exists. Refresh and try again.");
                }
            },
            "Memory updated.",
            selected.Id);
    }

    private void DeleteMemory()
    {
        if (_memories.SelectedItem is not CompanionMemoryItem selected)
        {
            ShowError("Select a memory to delete.");
            return;
        }

        if (MessageBox.Show(
                "Delete the selected memory? This cannot be undone.",
                "OpenClicky companion memory",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        Mutate(
            () =>
            {
                if (!CompanionMemoryStore.DeleteFact(selected.Id))
                {
                    throw new InvalidOperationException("That memory no longer exists. Refresh and try again.");
                }
            },
            "Memory deleted.");
        if (_canEdit && !_error.Visible)
        {
            _memoryText.Clear();
        }
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export companion memory",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = "json",
            AddExtension = true,
            FileName = "openclicky-companion-memory.json",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (!CompanionMemoryStore.ExportTo(dialog.FileName))
            {
                ShowError("There is no companion memory file to export.");
                return;
            }
            if (_canEdit)
            {
                ShowStatus("Companion memory exported.");
            }
            else
            {
                _status.Text = "Companion memory exported. Editing remains locked until you clear the unreadable file.";
            }
        }
        catch (Exception ex)
        {
            ShowError("Companion memory could not be exported: " + ex.Message);
        }
    }

    private void Clear()
    {
        if (MessageBox.Show(
            "Clear the companion profile and every manual or learned memory? This cannot be undone. Export first if you need a copy.",
                "Clear companion memory",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        try
        {
            CompanionMemoryStore.Clear();
            RefreshMemory();
            ShowStatus("Companion memory cleared.");
        }
        catch (Exception ex)
        {
            ShowError("Companion memory could not be cleared: " + ex.Message);
            return;
        }
        NotifyChanged();
    }

    private void Mutate(Action mutation, string success, string? selectedId = null)
    {
        try
        {
            mutation();
            var result = CompanionMemoryStore.Load();
            ApplyLoadResult(result);
            if (selectedId is not null && result.Memory is not null)
            {
                FillMemories(result.Memory.Facts, selectedId);
            }
            ShowStatus(success);
        }
        catch (Exception ex)
        {
            RefreshMemory();
            ShowError("Companion memory could not be saved: " + ex.Message);
            return;
        }
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        try
        {
            _changed?.Invoke();
        }
        catch (Exception ex)
        {
            ShowError("Memory was saved, but the app could not refresh it: " + ex.Message);
        }
    }

    private void SetEditorsReadOnly(bool readOnly)
    {
        _name.ReadOnly = readOnly;
        _personality.ReadOnly = readOnly;
        _instructions.ReadOnly = readOnly;
        _memoryText.ReadOnly = readOnly;
        _saveProfile.Enabled = !readOnly;
        _addMemory.Enabled = !readOnly;
    }

    private void RefreshButtons()
    {
        var selected = _memories.SelectedItem is CompanionMemoryItem;
        _saveProfile.Enabled = _canEdit;
        _addMemory.Enabled = _canEdit && _memories.Items.Count < CompanionMemoryStore.MaxFacts;
        _updateMemory.Enabled = _canEdit && selected;
        _deleteMemory.Enabled = _canEdit && selected;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
        _status.Text = "Review the error above; no existing memory was intentionally replaced.";
    }

    private void ShowStatus(string message)
    {
        _error.Visible = false;
        _status.Text = message;
    }

    private static TableLayoutPanel Section()
    {
        var section = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new Padding(0, 8, 0, 16),
            Padding = new Padding(16),
            BackColor = Color.White,
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return section;
    }

    private static Label Heading(string text, float size)
    {
        var label = AppTheme.Label(text, size, true);
        label.Dock = DockStyle.Fill;
        label.Margin = new Padding(0, 0, 0, 10);
        return label;
    }

    private static TextBox AddTextBox(
        TableLayoutPanel parent,
        string label,
        int maxLength,
        int height,
        bool multiline = false)
    {
        var caption = AppTheme.Label(label, 9, true);
        caption.Dock = DockStyle.Fill;
        caption.Margin = new Padding(0, 4, 0, 4);
        parent.Controls.Add(caption, 0, parent.RowCount++);
        var box = new TextBox
        {
            Dock = DockStyle.Fill,
            Height = height,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            AcceptsReturn = multiline,
            MaxLength = maxLength,
            AccessibleName = label,
            BackColor = Color.White,
            ForeColor = AppTheme.Ink,
            Margin = new Padding(0, 0, 0, 10),
        };
        parent.Controls.Add(box, 0, parent.RowCount++);
        return box;
    }

    private static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 4, 0, 0),
        };
        row.Controls.AddRange(buttons);
        return row;
    }
}
