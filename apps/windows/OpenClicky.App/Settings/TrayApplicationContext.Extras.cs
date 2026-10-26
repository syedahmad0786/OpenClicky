using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenClicky.Agent;
using OpenClicky.App.Settings;
using OpenClicky.Core;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.App;

/// <summary>
/// The notch's Extras pages: Notes, Clipboard shelf, Tasks and Memory. Their data reaches the page through
/// <see cref="SettingsRuntime"/>; their settings.action intents land here. Clipboard text is user data: never logged.
/// </summary>
internal sealed partial class TrayApplicationContext
{
    // Read from disk on first push and again when their page opens or this app changes them; the page needs no SQLite read per push.
    private SettingsNotes? _notesView;
    private SettingsMemory? _memoryView;
    /// <summary>The one shelf writer while the classic shelf window is closed (that window opens its own).</summary>
    private ClipboardShelf? _shelf;
    private string? _shelfError;
    private ClipboardCollector? _collector;
    private bool _shelfFullToasted;

    private SettingsNotes NotesView => _notesView ??= ShellSource("notes", () => ExtrasViews.Notes(LocalNotesStore.Load()), ExtrasViews.NotesUnavailable(null));
    private SettingsMemory MemoryView => _memoryView ??= ShellSource("memory", () => ExtrasViews.Memory(CompanionMemoryStore.Load()), ExtrasViews.MemoryUnavailable(null));

    private void ForgetNotesAndMemory()
    {
        _notesView = null;
        _memoryView = null;
    }

    private SettingsTasks TasksView() =>
        ExtrasViews.Tasks(_taskRuns, TaskOpen, ResumeTargets().Count > 0);

    private SettingsClipboard ClipboardView()
    {
        if (_clipboardShelf is { IsDisposed: false })
            return ExtrasViews.ClipboardUnavailable("The classic clipboard shelf window is open. Close it to manage the shelf here.");
        if (Shelf is not { } shelf) return ExtrasViews.ClipboardUnavailable(_shelfError ?? "Saved clipboard items could not be opened.");
        return ExtrasViews.Clipboard(shelf.Items, shelf.PeekNext()?.Id, _collector?.On == true);
    }

    /// <summary>Opened on first use; a failure is remembered (as the page's error) until the classic window hands the shelf back.</summary>
    private ClipboardShelf? Shelf
    {
        get
        {
            if (_shelf is not null || _shelfError is not null) return _shelf;
            try { _shelf = new ClipboardShelf(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log("clipboard_shelf_open_error=" + ex.GetType().Name);
                _shelfError = "Saved clipboard items could not be opened: " + ex.Message;
            }
            return _shelf;
        }
    }

    /// <summary>The classic shelf window opens its own copy of the shelf: stop collecting here and let go of ours so only one writes.</summary>
    private void ReleaseShelf()
    {
        _collector?.Stop();
        _shelf = null;
        _shelfError = null;
    }

    /// <summary>openNotes / openClipboard / openTasks: the notch's own page (the classic window only when the shell is not running).</summary>
    private void OpenExtrasPage(string section)
    {
        if (_shell is { IsActive: true }) { _shell.OpenNotch(NotchTab.Settings, section); return; }
        if (section == "clipboard") { ShowClipboardShelf(); return; }
        ShowClassic();
        if (section == "notes") _home.ShowNotes();
        else _home.ShowTasks();
    }

    private void ExtrasAction(string action, string? arg)
    {
        try
        {
            switch (action.Split('.')[0])
            {
                case "note": NoteAction(action, arg); break;
                case "memory": MemoryAction(action, arg); break;
                case "clipboard": ClipboardAction(action, arg); break;
                case "task": TaskAction(action, arg); break;
            }
        }
        finally { RequestShellPush(); } // also after a failure: a page waiting on its Save button stops waiting
    }

    private void NoteAction(string action, string? arg)
    {
        switch (action)
        {
            case "note.save":
                if (!ExtrasArgs.TryNote(arg, out var id, out var title, out var text)) { _shell?.AddToast("A note needs a title of up to 100 characters and at most 10,000 characters of text."); return; }
                _notesView = null;
                LocalNotesStore.SaveNote(id, title, text);
                _shell?.AddToast(id is null ? "Note added." : "Note saved.");
                break;
            case "note.delete":
                if (!ExtrasArgs.IsId(arg)) return;
                _notesView = null;
                _shell?.AddToast(LocalNotesStore.Delete(arg!) ? "Note deleted." : "That note no longer exists.");
                break;
            case "note.export":
                if (SaveAs("Export local notes", "Markdown files (*.md)|*.md|All files (*.*)|*.*", "md", "openclicky-notes.md") is not { } path) return;
                LocalNotesStore.ExportMarkdown(path);
                _shell?.AddToast("Notes exported as Markdown.");
                break;
        }
    }

    private void MemoryAction(string action, string? arg)
    {
        _memoryView = null;
        switch (action)
        {
            case "memory.profile":
                if (!ExtrasArgs.TryProfile(arg, out var name, out var personality, out var instructions)) { _shell?.AddToast("That profile is too long to save."); return; }
                CompanionMemoryStore.SetProfile(name, personality, instructions);
                _shell?.AddToast("Companion profile saved.");
                break;
            case "memory.add":
                if (ExtrasArgs.Fact(arg) is not { } fact) { _shell?.AddToast("A memory is up to 1,000 characters."); return; }
                CompanionMemoryStore.AddFact(fact);
                _shell?.AddToast("Memory added.");
                break;
            case "memory.update":
                if (!ExtrasArgs.TryIdText(arg, CompanionMemoryStore.MaxFactLength, out var id, out var text)) { _shell?.AddToast("A memory is up to 1,000 characters."); return; }
                _shell?.AddToast(CompanionMemoryStore.UpdateFact(id, text.Trim()) ? "Memory updated." : "That memory no longer exists.");
                break;
            case "memory.delete":
                if (!ExtrasArgs.IsId(arg)) return;
                _shell?.AddToast(CompanionMemoryStore.DeleteFact(arg!) ? "Memory deleted." : "That memory no longer exists.");
                break;
            case "memory.clear":
                CompanionMemoryStore.Clear();
                _shell?.AddToast("Companion memory cleared.");
                break;
        }
    }

    private void ClipboardAction(string action, string? arg)
    {
        if (_clipboardShelf is { IsDisposed: false }) { _shell?.AddToast("Close the classic clipboard shelf window to manage the shelf here."); return; }
        if (Shelf is not { } shelf) { _shell?.AddToast(_shelfError ?? "Saved clipboard items could not be opened."); return; }
        var item = ExtrasArgs.ClipId(arg) is { } clipId ? shelf.Items.FirstOrDefault(i => i.Id == clipId) : null;
        bool NeedItem()
        {
            if (item is null) _shell?.AddToast("That item is no longer on the shelf.");
            return item is not null;
        }
        switch (action)
        {
            case "clipboard.collect": SetCollecting(shelf, arg == "on"); break;
            case "clipboard.add":
                if (ExtrasArgs.ClipText(arg) is not { } text) { _shell?.AddToast("Add up to 10,000 characters of text."); return; }
                _shell?.AddToast(AddResult(shelf.TryAdd(text, "Added in OpenClicky")));
                break;
            case "clipboard.edit":
                if (!ExtrasArgs.TryIdText(arg, ExtrasArgs.MaxClipText, out var editId, out var edited) || ExtrasArgs.ClipId(editId) is not { } targetId
                    || shelf.Items.FirstOrDefault(i => i.Id == targetId) is not { } target) { _shell?.AddToast("That edit could not be saved."); return; }
                if (target.Text.Length > ExtrasViews.ClipPreview) { _shell?.AddToast("Long items are edited in the classic clipboard shelf."); return; }
                shelf.Edit(target.Id, edited);
                _shell?.AddToast("Edit saved.");
                break;
            case "clipboard.copy": if (NeedItem()) CopyToClipboard(item!.Text, "Copied. Press Ctrl+V where you want it."); break;
            case "clipboard.copyAll":
                if (shelf.Items.Count == 0) { _shell?.AddToast("There is nothing on the shelf to copy."); return; }
                CopyToClipboard(shelf.Combine(Environment.NewLine), "All items copied. Press Ctrl+V where you want them.");
                break;
            case "clipboard.copyNext":
                if (shelf.PeekNext() is not { } next) { _shell?.AddToast("There is nothing on the shelf to copy."); return; }
                if (CopyToClipboard(next.Text, "Next item copied. Press Ctrl+V where you want it.")) shelf.AdvanceNext(next.Id);
                break;
            case "clipboard.pin" or "clipboard.unpin": if (NeedItem()) shelf.SetPinned(item!.Id, action == "clipboard.pin"); break;
            case "clipboard.moveUp" or "clipboard.moveDown":
                if (!NeedItem()) return;
                var to = shelf.Items.ToList().FindIndex(i => i.Id == item!.Id) + (action == "clipboard.moveUp" ? -1 : 1);
                if (to >= 0 && to < shelf.Items.Count) shelf.Move(item!.Id, to);
                break;
            case "clipboard.delete": if (NeedItem()) { shelf.Remove(item!.Id); _shell?.AddToast("Item deleted from this PC."); } break;
            case "clipboard.clear":
                foreach (var each in shelf.Items.Select(i => i.Id).ToList()) shelf.Remove(each);
                _shell?.AddToast("Clipboard shelf cleared.");
                break;
        }
    }

    private static string AddResult(ClipboardShelfAddResult result) => result switch
    {
        ClipboardShelfAddResult.Added => "Saved on the shelf.",
        ClipboardShelfAddResult.DuplicateIgnored => "That is already the last item on the shelf.",
        ClipboardShelfAddResult.Full => $"The shelf is full ({ClipboardShelf.MaxItems} items). Delete an item to add more.",
        _ => "That text is over the shelf's size limit.",
    };

    private void SetCollecting(ClipboardShelf shelf, bool on)
    {
        if (on)
        {
            _collector ??= new ClipboardCollector(CollectClipboard); // WM_CLIPBOARDUPDATE arrives on the UI thread
            _collector.Start(); // throws Win32Exception if Windows refuses the listener; the action's catch toasts it
            _shelfFullToasted = false;
        }
        else _collector?.Stop();
        shelf.SetCollecting(on); // the classic shelf window resumes from this when it opens
        _shell?.AddToast(on ? "Collecting copied text. It stays on this PC; protected windows are skipped." : "Clipboard collection is off.");
    }

    /// <summary>One copy in another app: saved unless OpenClicky made it, OpenClicky is in front, or the window is protected.</summary>
    private void CollectClipboard()
    {
        if (_collector?.On != true || _shelf is not { } shelf || _clipboardShelf is { IsDisposed: false }) return;
        if (ClipboardCollector.OwnedByThisProcess() || ForegroundWindowInfo.IsCurrentProcessForeground() || ClipboardCollector.IsMarkedPrivate()) return;
        var source = ForegroundWindowInfo.GetTitle();
        if (AgentDenyList.IsDenied(source, AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra))) return;
        string text;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText)) return;
            text = Clipboard.GetText(TextDataFormat.UnicodeText);
        }
        catch (ExternalException) { return; } // the clipboard was busy: that copy is skipped
        try
        {
            var result = shelf.TryAdd(text, source);
            if (result == ClipboardShelfAddResult.Added) RequestShellPush();
            else if (result == ClipboardShelfAddResult.Full && !_shelfFullToasted) { _shelfFullToasted = true; _shell?.AddToast(AddResult(result)); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Log("clipboard_collect_error=" + ex.GetType().Name); }
    }

    private bool CopyToClipboard(string text, string done)
    {
        if (text.Length == 0) { _shell?.AddToast("That item is empty."); return false; }
        try { Clipboard.SetText(text, TextDataFormat.UnicodeText); }
        catch (ExternalException) { _shell?.AddToast("The clipboard is busy; nothing was copied. Try again."); return false; }
        _shell?.AddToast(done);
        return true;
    }

    private void TaskAction(string action, string? arg)
    {
        switch (action)
        {
            case "task.pause":
                if (!TaskOpen) { _shell?.AddToast("No task is running."); return; }
                PauseTaskUnlessConfirming();
                break;
            case "task.resume":
                if (ResumeTargets().Count == 0) { _shell?.AddToast("Nothing is paused right now."); return; }
                _shell?.AddToast("Resuming in 3 seconds. Keep the task's window in front.");
                _ = ResumeTaskAsync();
                break;
            case "task.stop":
                if (!TaskOpen) { _shell?.AddToast("No task is running."); return; }
                CancelTask();
                _shell?.AddToast("Task stopped.");
                break;
            case "task.export":
                if (_taskRuns.FirstOrDefault(r => r.Id == arg) is not { } run) { _shell?.AddToast("That task is no longer in the history."); return; }
                if (SaveAs("Export task events", "Task events (*.ndjson)|*.ndjson", "ndjson", "openclicky-task.ndjson") is not { } path) return;
                File.WriteAllText(path, AgUiTaskEvents.Encode(run));
                _shell?.AddToast("Task events exported.");
                break;
            case "task.clear":
                ClearTasks();
                _shell?.AddToast("Task history cleared.");
                break;
        }
    }

    /// <summary>A native Save dialog over the notch; null when cancelled or another dialog is open.</summary>
    private string? SaveAs(string title, string filter, string extension, string fileName)
    {
        using var dialog = new SaveFileDialog { Title = title, Filter = filter, DefaultExt = extension, AddExtension = true, FileName = fileName, OverwritePrompt = true };
        return Modal(() => { using var owner = NativeSettings.TopMostOwner(); return dialog.ShowDialog(owner) == DialogResult.OK; }, false) ? dialog.FileName : null;
    }

    private void DisposeExtras()
    {
        _collector?.Dispose();
        _collector = null;
    }
}

/// <summary>A message-only window on the clipboard format listener; <see cref="Start"/> and <see cref="Stop"/> on the UI thread.</summary>
internal sealed class ClipboardCollector(Action changed) : NativeWindow, IDisposable
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly IntPtr MessageOnly = new(-3);

    public bool On => Handle != IntPtr.Zero;

    public void Start()
    {
        if (On) return;
        CreateHandle(new CreateParams { Parent = MessageOnly });
        if (AddClipboardFormatListener(Handle)) return;
        var error = Marshal.GetLastWin32Error();
        DestroyHandle();
        throw new Win32Exception(error);
    }

    public void Stop()
    {
        if (!On) return;
        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
    }

    public void Dispose() => Stop();

    private static readonly uint ExcludeFromMonitors = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint ViewerIgnore = RegisterClipboardFormat("Clipboard Viewer Ignore");
    private static readonly uint IncludeInHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");

    /// <summary>
    /// The copy is marked private by the app that made it, the way password managers and Windows' own clipboard history agree on:
    /// "ExcludeClipboardContentFromMonitorProcessing" or "Clipboard Viewer Ignore" present, or "CanIncludeInClipboardHistory" set to 0.
    /// Such a copy is never collected.
    /// </summary>
    public static bool IsMarkedPrivate()
    {
        if (IsClipboardFormatAvailable(ExcludeFromMonitors) || IsClipboardFormatAvailable(ViewerIgnore)) return true;
        if (!IsClipboardFormatAvailable(IncludeInHistory)) return false;
        try
        {
            return Clipboard.GetData("CanIncludeInClipboardHistory") is not MemoryStream stream
                || stream.Length < 4 || BitConverter.ToUInt32(stream.ToArray(), 0) == 0;
        }
        catch (ExternalException) { return true; } // unreadable flag: treat as private
    }

    /// <summary>The clipboard was last written by a window of this process (a Copy button, dictation, Talk's copy).</summary>
    public static bool OwnedByThisProcess()
    {
        var owner = GetClipboardOwner();
        return owner != IntPtr.Zero && GetWindowThreadProcessId(owner, out var pid) != 0 && pid == (uint)Environment.ProcessId;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmClipboardUpdate) changed();
        base.WndProc(ref m);
    }

    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string format);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
