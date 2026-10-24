using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class NotesPanel : UserControl
{
    private readonly Action? _changed;
    private readonly TextBox _search;
    private readonly ListBox _notes;
    private readonly TextBox _title;
    private readonly TextBox _text;
    private readonly Button _add;
    private readonly Button _update;
    private readonly Button _delete;
    private readonly Button _export;
    private readonly Label _count;
    private readonly Label _status;
    private readonly Label _error;
    private bool _canEdit;
    private int _totalCount;

    public NotesPanel(Action? changed = null)
    {
        _changed = changed;
        Dock = DockStyle.Fill;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.Ink;
        Font = new Font("Segoe UI", 10f);
        AccessibleName = "Local notes";
        AutoScroll = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 650,
            ColumnCount = 2,
            RowCount = 5,
            Padding = new Padding(24, 20, 24, 20),
            BackColor = AppTheme.Background,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = AppTheme.Label("Notes", 19, true);
        heading.Dock = DockStyle.Fill;
        layout.Controls.Add(heading, 0, 0);
        layout.SetColumnSpan(heading, 2);
        var privacy = AppTheme.Label(
            "Notes are plain-text JSON stored only on this PC. There is no cloud sync, and AI providers do not read them automatically. " +
            "Say “note: <text>” only when you want OpenClicky to save a note.", 9);
        privacy.Dock = DockStyle.Fill;
        privacy.MaximumSize = new Size(900, 0);
        layout.Controls.Add(privacy, 0, 1);
        layout.SetColumnSpan(privacy, 2);

        _error = AppTheme.Label(string.Empty, 9, true);
        _error.ForeColor = Color.FromArgb(176, 35, 35);
        _error.Dock = DockStyle.Fill;
        _error.Visible = false;
        _error.AccessibleName = "Notes error";
        layout.Controls.Add(_error, 0, 2);
        layout.SetColumnSpan(_error, 2);

        var left = Section();
        _search = Field("Search notes", LocalNotesStore.MaxTextLength, multiline: false);
        _search.PlaceholderText = "Search title or text";
        _search.TextChanged += (_, _) => RefreshList();
        left.Controls.Add(_search, 0, left.RowCount++);
        _count = AppTheme.Label($"0 of {LocalNotesStore.MaxNotes} notes", 8);
        _count.AccessibleName = "Note count";
        left.Controls.Add(_count, 0, left.RowCount++);
        _notes = new ListBox
        {
            Dock = DockStyle.Fill,
            DisplayMember = nameof(LocalNote.Title),
            AccessibleName = "Saved local notes",
            IntegralHeight = false,
            BackColor = Color.White,
            ForeColor = AppTheme.Ink,
        };
        left.Controls.Add(_notes, 0, left.RowCount++);
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var right = Section();
        _title = LabeledField(right, "Title", LocalNotesStore.MaxTitleLength, 30);
        _text = LabeledField(right, "Note", LocalNotesStore.MaxTextLength, 230, multiline: true);
        _add = AppTheme.Button("Add note", true);
        _add.Click += (_, _) => Add();
        _update = AppTheme.Button("Update selected");
        _update.Click += (_, _) => UpdateSelected();
        _delete = AppTheme.Button("Delete selected");
        _delete.Click += (_, _) => Delete();
        right.Controls.Add(ButtonRow(_add, _update, _delete), 0, right.RowCount++);

        _notes.SelectedIndexChanged += (_, _) =>
        {
            if (_notes.SelectedItem is LocalNote note)
            {
                _title.Text = note.Title;
                _text.Text = note.Text;
            }
            RefreshButtons();
        };

        layout.Controls.Add(left, 0, 3);
        layout.Controls.Add(right, 1, 3);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 0),
        };
        _export = AppTheme.Button("Export Markdown");
        _export.Click += (_, _) => ExportMarkdown();
        _status = AppTheme.Label("Loading notes…", 9);
        _status.Margin = new Padding(12, 10, 0, 0);
        _status.AccessibleName = "Notes status";
        footer.Controls.AddRange(new Control[] { _export, _status });
        layout.Controls.Add(footer, 0, 4);
        layout.SetColumnSpan(footer, 2);

        Controls.Add(layout);
        RefreshNotes();
    }

    public void RefreshNotes()
    {
        if (IsDisposed)
        {
            return;
        }
        if (IsHandleCreated && InvokeRequired)
        {
            BeginInvoke(RefreshNotes);
            return;
        }

        try
        {
            var result = LocalNotesStore.Load();
            if (!result.CanEdit || result.Notes is null)
            {
                ApplyUnreadable(result.Error ?? "The saved notes could not be read and were left unchanged.");
                return;
            }
            _canEdit = true;
            _totalCount = result.Notes.Count;
            _error.Visible = false;
            SetEditorsReadOnly(false);
            RefreshList(result.Notes);
            _export.Enabled = result.Status == LocalNotesLoadStatus.Loaded && result.Notes.Count > 0;
            _status.Text = result.Status == LocalNotesLoadStatus.Missing
                ? "No notes saved yet."
                : "Notes are saved locally on this PC.";
            RefreshButtons();
        }
        catch (Exception ex)
        {
            ApplyUnreadable("Notes could not be accessed and were left unchanged: " + ex.Message);
        }
    }

    private void ApplyUnreadable(string message)
    {
        _canEdit = false;
        _totalCount = 0;
        _notes.Items.Clear();
        _title.Clear();
        _text.Clear();
        SetEditorsReadOnly(true);
        _export.Enabled = false;
        _error.Text = message;
        _error.Visible = true;
        _status.Text = "Editing is locked so the unreadable notes file cannot be overwritten.";
        _count.Text = $"0 of {LocalNotesStore.MaxNotes} notes";
        RefreshButtons();
    }

    private void RefreshList() => RefreshList(null);

    private void RefreshList(IReadOnlyList<LocalNote>? loaded)
    {
        if (!_canEdit)
        {
            return;
        }
        try
        {
            var selectedId = (_notes.SelectedItem as LocalNote)?.Id;
            var records = loaded ?? LocalNotesStore.GetRecords();
            if (!string.IsNullOrWhiteSpace(_search.Text))
            {
                records = records.Where(note =>
                        note.Title.Contains(_search.Text, StringComparison.OrdinalIgnoreCase)
                        || note.Text.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            _notes.BeginUpdate();
            try
            {
                _notes.Items.Clear();
                foreach (var note in records.OrderByDescending(note => note.UpdatedAtUtc))
                {
                    _notes.Items.Add(note);
                    if (note.Id == selectedId)
                    {
                        _notes.SelectedItem = note;
                    }
                }
            }
            finally
            {
                _notes.EndUpdate();
            }
            _count.Text = string.IsNullOrWhiteSpace(_search.Text)
                ? $"{records.Count} of {LocalNotesStore.MaxNotes} notes"
                : $"{records.Count} matching notes";
        }
        catch (Exception ex)
        {
            ApplyUnreadable("Notes could not be searched and were left unchanged: " + ex.Message);
        }
        RefreshButtons();
    }

    private void Add()
    {
        Mutate(() => LocalNotesStore.SaveNote(null, _title.Text, _text.Text), "Note added.");
        if (_canEdit && !_error.Visible)
        {
            ClearEditor();
        }
    }

    private void UpdateSelected()
    {
        if (_notes.SelectedItem is not LocalNote selected)
        {
            ShowError("Select a note to update.");
            return;
        }
        Mutate(() =>
        {
            if (!LocalNotesStore.Update(selected.Id, _title.Text, _text.Text))
            {
                throw new InvalidOperationException("That note no longer exists. Refresh and try again.");
            }
        }, "Note updated.");
    }

    private void Delete()
    {
        if (_notes.SelectedItem is not LocalNote selected)
        {
            ShowError("Select a note to delete.");
            return;
        }
        if (MessageBox.Show(
                "Delete the selected note? This cannot be undone.",
                "OpenClicky local notes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        Mutate(() =>
        {
            if (!LocalNotesStore.Delete(selected.Id))
            {
                throw new InvalidOperationException("That note no longer exists. Refresh and try again.");
            }
        }, "Note deleted.");
        if (_canEdit && !_error.Visible)
        {
            ClearEditor();
        }
    }

    private void ExportMarkdown()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export local notes",
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            DefaultExt = "md",
            AddExtension = true,
            FileName = "openclicky-notes.md",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
        {
            return;
        }
        try
        {
            LocalNotesStore.ExportMarkdown(dialog.FileName);
            ShowStatus("Notes exported as Markdown.");
        }
        catch (Exception ex)
        {
            ShowError("Notes could not be exported: " + ex.Message);
        }
    }

    private void Mutate(Action mutation, string success)
    {
        try
        {
            mutation();
            RefreshNotes();
            if (!_canEdit)
            {
                return;
            }
            ShowStatus(success);
        }
        catch (Exception ex)
        {
            RefreshNotes();
            ShowError("Notes could not be saved: " + ex.Message);
            return;
        }
        try
        {
            _changed?.Invoke();
        }
        catch (Exception ex)
        {
            ShowError("The note was saved, but the app could not refresh it: " + ex.Message);
        }
    }

    private void SetEditorsReadOnly(bool readOnly)
    {
        _search.ReadOnly = readOnly;
        _title.ReadOnly = readOnly;
        _text.ReadOnly = readOnly;
    }

    private void RefreshButtons()
    {
        var selected = _notes.SelectedItem is LocalNote;
        _add.Enabled = _canEdit && _totalCount < LocalNotesStore.MaxNotes;
        _update.Enabled = _canEdit && selected;
        _delete.Enabled = _canEdit && selected;
    }

    private void ClearEditor()
    {
        _notes.ClearSelected();
        _title.Clear();
        _text.Clear();
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = true;
        _status.Text = "Review the error above; the existing notes file was not intentionally replaced.";
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
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 0,
            Margin = new Padding(0, 8, 12, 0),
            Padding = new Padding(14),
            BackColor = Color.White,
        };
        section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return section;
    }

    private static TextBox LabeledField(TableLayoutPanel parent, string label, int maxLength, int height, bool multiline = false)
    {
        var caption = AppTheme.Label(label, 9, true);
        caption.Dock = DockStyle.Fill;
        caption.Margin = new Padding(0, 0, 0, 4);
        parent.Controls.Add(caption, 0, parent.RowCount++);
        var field = Field(label, maxLength, multiline);
        field.Height = height;
        field.Margin = new Padding(0, 0, 0, 12);
        parent.Controls.Add(field, 0, parent.RowCount++);
        return field;
    }

    private static TextBox Field(string name, int maxLength, bool multiline)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
            AcceptsReturn = multiline,
            MaxLength = maxLength,
            AccessibleName = name,
            BackColor = Color.White,
            ForeColor = AppTheme.Ink,
        };
    }

    private static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        row.Controls.AddRange(buttons);
        return row;
    }
}
