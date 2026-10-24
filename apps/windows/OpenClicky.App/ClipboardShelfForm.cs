using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenClicky.Agent;
using OpenClicky.Core;

namespace OpenClicky.App;

internal sealed class ClipboardShelfForm : Form
{
    private const int WmClipboardUpdate = 0x031D;
    private readonly ClipboardShelf? _shelf;
    private readonly IReadOnlyList<string> _excluded;
    private readonly ListBox _items = new() { Dock = DockStyle.Fill, IntegralHeight = false, BackColor = Color.White, ForeColor = AppTheme.Ink, AccessibleName = "Saved clipboard items" };
    private readonly TextBox _content = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, BackColor = Color.White, ForeColor = AppTheme.Ink, AcceptsReturn = true, AcceptsTab = true, MaxLength = ClipboardShelf.MaxItemLength, AccessibleName = "Exact selected item content" };
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "Search saved text or source", AccessibleName = "Search clipboard shelf" };
    private readonly TextBox _manual = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, MaxLength = ClipboardShelf.MaxItemLength, BackColor = Color.White, ForeColor = AppTheme.Ink, AccessibleName = "Text to save manually", PlaceholderText = "Type or paste text to save" };
    private readonly Label _count;
    private readonly Label _status;
    private readonly Button _startStop = AppTheme.Button("Start collecting", true);
    private readonly Button _copySelected = AppTheme.Button("Copy selected", true);
    private readonly Button _copyCombined = AppTheme.Button("Copy all");
    private readonly Button _copyNext = AppTheme.Button("Copy next");
    private readonly Button _chooseDestination = AppTheme.Button("Choose destination", true);
    private readonly Button _pasteNext = AppTheme.Button("Paste next", true);
    private readonly Button _pasteAll = AppTheme.Button("Paste all");
    private readonly Button _stopPaste = AppTheme.Button("Stop paste");
    private readonly Button _save = AppTheme.Button("Save edit", true);
    private readonly Button _pin = AppTheme.Button("Pin");
    private readonly Button _moveUp = AppTheme.Button("Move up");
    private readonly Button _moveDown = AppTheme.Button("Move down");
    private readonly Button _remove = AppTheme.Button("Delete selected");
    private bool _collecting, _listenerRegistered;
    private DateTimeOffset _ignoreClipboardUntilUtc;
    private bool _inserting, _uncertainWrite;
    private string? _loadError;
    private DictationTarget? _destination;
    private CancellationTokenSource? _selection;
    private CancellationTokenSource? _paste;
    private readonly IAgentActionExecutor _executor;

    public ClipboardShelfForm() : this(null, null, null) { }

    internal ClipboardShelfForm(string? shelfPath, IReadOnlyList<string>? excluded, IAgentActionExecutor? executor = null)
    {
        _executor = executor ?? new CompositeAgentExecutor();
        _excluded = excluded ?? AgentDenyList.ParseExtraFragments(SettingsStore.Load().AgentDenyListExtra);
        try { _shelf = new ClipboardShelf(shelfPath); }
        catch (Exception ex) { _loadError = "Saved clipboard items could not be opened: " + ex.Message; }

        Text = "OpenClicky clipboard shelf"; AppTheme.Apply(this); StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(620, 780); MinimumSize = new Size(480, 620); ShowInTaskbar = true; AccessibleName = "Clipboard shelf";
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 11, Padding = new Padding(20) };
        foreach (var style in new[] { SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize, SizeType.AutoSize, SizeType.Percent, SizeType.AutoSize, SizeType.AutoSize })
            layout.RowStyles.Add(new RowStyle(style, style == SizeType.Percent ? 1 : 0));
        layout.Controls.Add(AppTheme.Label("Clipboard shelf", 19, true));
        var privacy = AppTheme.Label("Collected text stays on this computer until you delete it. It is not sent to AI. Copy buttons put text on the Windows clipboard so you can press Ctrl+V in your chosen app.", 9);
        privacy.MaximumSize = new Size(570, 0); layout.Controls.Add(privacy); layout.Controls.Add(_search); layout.Controls.Add(_items); layout.Controls.Add(_content);
        layout.Controls.Add(Row(_save, _pin, _moveUp, _moveDown));
        layout.Controls.Add(Row(_copySelected, _copyCombined, _copyNext, _remove));
        layout.Controls.Add(Row(_chooseDestination, _pasteNext, _pasteAll, _stopPaste)); layout.Controls.Add(_manual);
        var add = AppTheme.Button("Add text"); _count = AppTheme.Label($"0 of {ClipboardShelf.MaxItems} items", 9, true); _count.Margin = new Padding(8, 10, 0, 0);
        layout.Controls.Add(Row(_startStop, add, _count));
        _status = AppTheme.Label(_loadError ?? "Collection is paused.", 9); _status.AccessibleName = "Clipboard shelf status"; layout.Controls.Add(_status);
        Controls.Add(layout); AcceptButton = add;

        _search.TextChanged += (_, _) => RefreshItems(); _items.SelectedIndexChanged += (_, _) => ShowSelected(); _content.TextChanged += (_, _) => RefreshButtons();
        add.Click += (_, _) => AddManual(); _startStop.Click += (_, _) => SetCollecting(!_collecting); _save.Click += (_, _) => SaveSelected();
        _pin.Click += (_, _) => TogglePinned(); _moveUp.Click += (_, _) => MoveSelected(-1); _moveDown.Click += (_, _) => MoveSelected(1);
        _copySelected.Click += (_, _) => { if (_items.SelectedItem is View item) CopyText(item.Item.Text, "Selected item copied. Press Ctrl+V in your destination."); };
        _copyCombined.Click += (_, _) => CopyText(_shelf?.Combine(Environment.NewLine) ?? "", "All items copied. Press Ctrl+V in your destination.");
        _copyNext.Click += (_, _) => CopyNext(); _remove.Click += (_, _) => RemoveSelected();
        _chooseDestination.Click += async (_, _) => await ChooseDestinationAsync();
        _pasteNext.Click += async (_, _) => await PasteAsync(all: false);
        _pasteAll.Click += async (_, _) => await PasteAsync(all: true);
        _stopPaste.Click += (_, _) => _paste?.Cancel();
        RefreshItems(); ResumeLayout(true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var screen = Screen.FromControl(Owner ?? this);
        BeginInvoke(() => PlaceOnScreen(screen));
        if (_shelf?.IsCollecting == true) SetCollecting(true);
    }

    private void PlaceOnScreen(Screen screen)
    {
        if (IsDisposed) return;
        var area = screen.WorkingArea;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        Location = new Point(area.Right - Width, area.Top);
    }

    protected override void WndProc(ref Message message) { base.WndProc(ref message); if (message.Msg == WmClipboardUpdate && _collecting) CollectClipboardText(); }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _selection?.Cancel(); _selection?.Dispose();
        _paste?.Cancel(); _paste?.Dispose();
        UnregisterListener(); base.OnFormClosed(e);
    }

    private void SetCollecting(bool enabled)
    {
        if (_shelf is null) { _status.Text = _loadError ?? "Saved clipboard items are unavailable."; return; }
        try
        {
            if (enabled && !_listenerRegistered)
            {
                if (!AddClipboardFormatListener(Handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
                _listenerRegistered = true;
            }
            else if (!enabled) UnregisterListener();
            _shelf.SetCollecting(enabled); _collecting = enabled;
            _startStop.AccessibleName = _startStop.Text = enabled ? "Pause collecting" : "Start collecting";
            _status.Text = enabled ? "Collecting copied text now." : "Collection is paused.";
        }
        catch (Exception ex) { UnregisterListener(); _status.Text = "Clipboard collection could not be changed: " + ex.Message; }
    }

    private void CollectClipboardText()
    {
        if (ConsumeAppClipboardWriteSuppression(DateTimeOffset.UtcNow)) return;
        if (ForegroundWindowInfo.IsCurrentProcessForeground()) return;
        var source = ForegroundWindowInfo.GetTitle();
        if (AgentDenyList.IsDenied(source, _excluded)) { _status.Text = "Copy ignored from a protected window."; return; }
        try { if (Clipboard.ContainsText(TextDataFormat.UnicodeText)) Add(Clipboard.GetText(TextDataFormat.UnicodeText), source); }
        catch (ExternalException ex) { _status.Text = "The clipboard is busy; that copy was not collected. Try copying it again. " + ex.Message; }
    }

    private void AddManual()
    {
        if (_manual.TextLength == 0) { _status.Text = "Enter text to add."; return; }
        if (Add(_manual.Text, "Added in OpenClicky")) _manual.Clear();
    }

    private bool Add(string text, string? source)
    {
        if (_shelf is null) return false;
        try
        {
            var result = _shelf.TryAdd(text, source);
            _status.Text = result switch
            {
                ClipboardShelfAddResult.Added => "Item saved locally.", ClipboardShelfAddResult.DuplicateIgnored => "Immediate duplicate ignored.",
                ClipboardShelfAddResult.Full => $"The shelf is full ({ClipboardShelf.MaxItems} items). Delete an item to continue.",
                _ => $"That item exceeds the shelf's {ClipboardShelf.MaxItemLength:N0}-character item or {ClipboardShelf.MaxTotalLength:N0}-character limit."
            };
            if (result != ClipboardShelfAddResult.Added) return false;
            RefreshItems(_shelf.Items[^1].Id); return true;
        }
        catch (Exception ex) { _status.Text = "The item could not be saved: " + ex.Message; return false; }
    }

    private void SaveSelected()
    {
        if (_shelf is null || _items.SelectedItem is not View selected) return;
        TryPersist(() => _shelf.Edit(selected.Item.Id, _content.Text), "Edit saved locally.", "The edit could not be saved", selected.Item.Id);
    }

    private void TogglePinned()
    {
        if (_shelf is null || _items.SelectedItem is not View selected) return;
        TryPersist(() => _shelf.SetPinned(selected.Item.Id, !selected.Item.IsPinned), selected.Item.IsPinned ? "Item unpinned." : "Item pinned.", "The pin could not be changed", selected.Item.Id);
    }

    private void MoveSelected(int direction)
    {
        if (_shelf is null || _items.SelectedItem is not View selected) return;
        var current = _shelf.Items.ToList().FindIndex(x => x.Id == selected.Item.Id); var destination = current + direction;
        if (current < 0 || destination < 0 || destination >= _shelf.Items.Count) return;
        TryPersist(() => _shelf.Move(selected.Item.Id, destination), direction < 0 ? "Item moved up." : "Item moved down.", "The item could not be moved", selected.Item.Id);
    }

    private void TryPersist(Func<bool> action, string success, string failure, long selectedId)
    {
        try { if (action()) { RefreshItems(selectedId); _status.Text = success; } }
        catch (Exception ex) { _status.Text = failure + ": " + ex.Message; }
    }

    private void CopyNext()
    {
        if (_shelf is null) return;
        try
        {
            var item = _shelf.PeekNext();
            if (item is null) _status.Text = "There are no items to copy.";
            else if (CopyText(item.Text, "Next item copied. Press Ctrl+V in your destination.")) _shelf.AdvanceNext(item.Id);
        }
        catch (Exception ex) { _status.Text = "The next item could not be selected: " + ex.Message; }
    }

    private bool CopyText(string text, string success)
    {
        if (text.Length == 0) { _status.Text = "The selected text is empty and cannot replace the Windows clipboard."; return false; }
        try { IgnoreNextAppClipboardWrite(); Clipboard.SetText(text, TextDataFormat.UnicodeText); _status.Text = success; return true; }
        catch (ExternalException ex) { _ignoreClipboardUntilUtc = default; _status.Text = "The clipboard is busy; nothing was copied. Try again. " + ex.Message; return false; }
    }

    internal void IgnoreNextAppClipboardWrite() => _ignoreClipboardUntilUtc = DateTimeOffset.UtcNow.AddSeconds(2);
    internal bool ConsumeAppClipboardWriteSuppression(DateTimeOffset now)
    {
        if (_ignoreClipboardUntilUtc < now) return false;
        _ignoreClipboardUntilUtc = default;
        return true;
    }

    private async Task ChooseDestinationAsync()
    {
        if (_inserting) return;
        _selection?.Cancel(); _selection?.Dispose();
        using var selection = _selection = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        _destination = null; _uncertainWrite = false;
        _status.Text = "Click a writable text field in another app. OpenClicky will return here after selecting it.";
        WindowState = FormWindowState.Minimized;
        try
        {
            while (!selection.IsCancellationRequested)
            {
                if (DictationTarget.TryCapture(out var target, out _, _excluded) && target is not null)
                {
                    _destination = target;
                    _status.Text = $"Destination chosen: {target.AppTitle} · {target.ControlName}. Paste inserts text only; it never presses Send or Enter.";
                    return;
                }
                await Task.Delay(250, selection.Token);
            }
        }
        catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "No destination was chosen. Try Choose destination again."; }
        finally
        {
            if (ReferenceEquals(_selection, selection)) _selection = null;
            if (!IsDisposed) { WindowState = FormWindowState.Normal; Show(); Activate(); RefreshButtons(); }
        }
    }

    private async Task PasteAsync(bool all)
    {
        if (_shelf is null || _inserting || _uncertainWrite) return;
        if (_destination is null) { _status.Text = "Choose a destination field first."; return; }
        var remaining = all ? _shelf.Items.Count : 1;
        if (remaining == 0) { _status.Text = "There are no items to paste."; return; }
        _paste?.Dispose();
        using var paste = _paste = new CancellationTokenSource();
        _inserting = true; RefreshButtons();
        var inserted = 0;
        var attemptInFlight = false;
        var verifiedButNotAdvanced = false;
        try
        {
            while (inserted < remaining && _shelf.PeekNext() is { } item)
            {
                paste.Token.ThrowIfCancellationRequested();
                attemptInFlight = true;
                var result = await _destination.InsertAsync(item.Text, _executor, paste.Token,
                    requireCurrentFocus: inserted > 0, exclusions: _excluded);
                attemptInFlight = false;
                if (!result.Verified)
                {
                    if (result.InputAttempted) _uncertainWrite = true;
                    else _destination = null;
                    _status.Text = result.Message + (result.InputAttempted
                        ? " Sequence stopped without advancing. Do not retry this item until you inspect the destination."
                        : " Sequence was preserved. Choose the destination again before retrying.");
                    return;
                }
                verifiedButNotAdvanced = true;
                if (!_shelf.AdvanceNext(item.Id))
                {
                    _uncertainWrite = true;
                    _status.Text = "Text was verified in the destination, but the saved sequence could not advance. Sequence stopped; do not retry until you inspect the destination.";
                    return;
                }
                verifiedButNotAdvanced = false;
                inserted++;
            }
            _status.Text = all ? $"Pasted and verified {inserted} items in order. No Send or Enter key was pressed."
                : "Pasted and verified the next item. No Send or Enter key was pressed.";
        }
        catch (OperationCanceledException)
        {
            if (attemptInFlight || verifiedButNotAdvanced) _uncertainWrite = true;
            _status.Text = attemptInFlight || verifiedButNotAdvanced
                ? "Paste was stopped during an input attempt. The current item was not advanced; inspect the destination before any retry."
                : "Paste stopped between items. The current item was not advanced.";
        }
        catch (Exception ex)
        {
            if (attemptInFlight || verifiedButNotAdvanced) _uncertainWrite = true;
            _status.Text = (attemptInFlight || verifiedButNotAdvanced
                ? "Paste failed during a possible input attempt. The current item was not advanced; inspect the destination before any retry: "
                : "Paste stopped before input. The current item was not advanced: ") + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_paste, paste)) _paste = null;
            _inserting = false;
            if (!IsDisposed) RefreshButtons();
        }
    }

    private void RemoveSelected()
    {
        if (_shelf is null || _items.SelectedItem is not View selected) return;
        try { _shelf.Remove(selected.Item.Id); RefreshItems(); _status.Text = "Selected item deleted from this computer."; }
        catch (Exception ex) { _status.Text = "The item could not be deleted: " + ex.Message; }
    }

    private void ShowSelected() { _content.Text = (_items.SelectedItem as View)?.Item.Text ?? ""; RefreshButtons(); }

    private void RefreshItems(long? selectId = null)
    {
        selectId ??= (_items.SelectedItem as View)?.Item.Id; var query = _search.Text; _items.BeginUpdate(); _items.Items.Clear();
        foreach (var item in _shelf?.Items ?? [])
        {
            if (query.Length > 0 && !item.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase) && !(item.Source?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)) continue;
            var view = new View(item); _items.Items.Add(view); if (item.Id == selectId) _items.SelectedItem = view;
        }
        _items.EndUpdate(); _count.Text = $"{_shelf?.Items.Count ?? 0} of {ClipboardShelf.MaxItems} items"; ShowSelected();
    }

    private void RefreshButtons()
    {
        var selected = _items.SelectedItem as View; var count = _shelf?.Items.Count ?? 0;
        _copySelected.Enabled = _pin.Enabled = _remove.Enabled = selected is not null;
        _save.Enabled = selected is not null && !string.Equals(_content.Text, selected.Item.Text, StringComparison.Ordinal);
        _pin.Text = selected?.Item.IsPinned == true ? "Unpin" : "Pin";
        var index = selected is null ? -1 : _shelf!.Items.ToList().FindIndex(x => x.Id == selected.Item.Id);
        _moveUp.Enabled = index > 0; _moveDown.Enabled = index >= 0 && index < count - 1; _copyCombined.Enabled = _copyNext.Enabled = count > 0; _startStop.Enabled = _shelf is not null;
        _chooseDestination.Enabled = !_inserting; _pasteNext.Enabled = _pasteAll.Enabled = count > 0 && _destination is not null && !_inserting && !_uncertainWrite;
        _stopPaste.Enabled = _inserting;
    }

    private void UnregisterListener() { if (_listenerRegistered) { _ = RemoveClipboardFormatListener(Handle); _listenerRegistered = false; } _collecting = false; }
    private static FlowLayoutPanel Row(params Control[] controls) { var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true }; row.Controls.AddRange(controls); return row; }

    private sealed record View(ClipboardShelfItem Item)
    {
        public override string ToString() { var text = Item.Text.Replace("\r", " ").Replace("\n", " "); if (text.Length > 55) text = text[..55] + "…"; return $"{(Item.IsPinned ? "★ " : "")}{text}  ·  {Item.Source ?? "Source unavailable"}"; }
    }

    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr window);
}
