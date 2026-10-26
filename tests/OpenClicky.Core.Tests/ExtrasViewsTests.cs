using System.Text.Json;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;

namespace OpenClicky.Core.Tests;

public class ExtrasViewsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

    private static SettingsView Clamped(SettingsView v) => ShellSettingsBuilder.Clamp(v);

    // ---- State

    [Fact]
    public void NotesAreNewestFirstAndAnUnreadableFileLocksEditing()
    {
        var notes = ExtrasViews.Notes(new(LocalNotesLoadStatus.Loaded, [new("a", "Old", "x", At), new("b", "New", "y", At.AddDays(1))], null));
        Assert.True(notes.Editable);
        Assert.Equal(["b", "a"], notes.Items.Select(n => n.Id));
        Assert.Equal("2026-10-25T09:30:00.000Z", notes.Items[0].UpdatedAt);
        Assert.Equal(LocalNotesStore.MaxNotes, notes.Limit);

        var locked = ExtrasViews.Notes(new(LocalNotesLoadStatus.Unreadable, null, "bad file"));
        Assert.False(locked.Editable);
        Assert.Equal("bad file", locked.Error);
        Assert.Empty(locked.Items);
    }

    [Fact]
    public void MemoryMarksLearnedFactsAndKeepsExportForAnUnreadableFile()
    {
        var memory = ExtrasViews.Memory(new(CompanionMemoryLoadStatus.Loaded,
            new("Clicky", "Warm", "Brief", [new("m", "Manual", At, At), new("l", "Learned", At, At, "conversation", "7")]), null));
        Assert.True(memory.Editable && memory.Saved);
        Assert.Equal([false, true], memory.Facts.Select(f => f.Learned));
        Assert.Equal("Clicky", memory.Name);

        Assert.False(ExtrasViews.Memory(new(CompanionMemoryLoadStatus.Missing, CompanionMemory.Empty, null)).Saved);
        var locked = ExtrasViews.Memory(new(CompanionMemoryLoadStatus.Unreadable, null, "bad"));
        Assert.False(locked.Editable);
        Assert.True(locked.Saved); // Export and Clear stay so the user can recover
    }

    [Fact]
    public void ClipboardCutsLongItemsAndNamesTheNextOne()
    {
        var items = new List<ClipboardShelfItem> { new(4, "short", "Outlook", At, IsPinned: true), new(9, new string('x', ExtrasViews.ClipPreview + 1), null, At) };
        var view = ExtrasViews.Clipboard(items, 9, collecting: true);
        Assert.True(view.Available && view.Collecting);
        Assert.Equal("9", view.NextId);
        Assert.Equal(["4", "9"], view.Items.Select(i => i.Id));
        Assert.False(view.Items[0].Truncated);
        Assert.True(view.Items[0].Pinned);
        Assert.True(view.Items[1].Truncated);
        Assert.Equal(ExtrasViews.ClipPreview, view.Items[1].Text.Length);

        var gone = ExtrasViews.ClipboardUnavailable("locked");
        Assert.False(gone.Available);
        Assert.Equal("locked", gone.Error);
    }

    [Fact]
    public void TasksAreNewestFirstWithTheirLastSteps()
    {
        var events = Enumerable.Range(1, 30).Select(i => new AgentProgress(i, "executing", "step " + i)).ToList();
        var runs = new List<TaskRunRecord> { new("old", "First", At, "completed", [], "Preview"), new("new", "Second", At.AddHours(1), "paused", events, "ConfirmEach") };
        var view = ExtrasViews.Tasks(runs, active: true, paused: true);
        Assert.True(view.Active && view.Paused);
        Assert.Equal(["new", "old"], view.Runs.Select(r => r.Id));
        Assert.Equal(ExtrasViews.TaskEvents, view.Runs[0].Events.Count);
        Assert.Equal("step 30", view.Runs[0].Events[^1].Message);
    }

    [Fact]
    public void ClampCapsEveryExtrasList()
    {
        var huge = new string('y', 20_000);
        var v = Clamped(SettingsView.Default with
        {
            Notes = new(true, huge, [.. Enumerable.Range(0, 30).Select(i => new NoteView("n" + i, huge, huge, "2026-10-24T09:30:00Z")), new NoteView("", "t", "x", "2026-10-24T09:30:00Z"), new NoteView("bad", "t", "x", "not a date")], 999),
            Clipboard = new(true, false, null, [.. Enumerable.Range(0, 150).Select(i => new ClipView((i + 1).ToString(), huge, false, huge, "2026-10-24T09:30:00Z", false))], "149", 5),
            Tasks = new(false, false, [.. Enumerable.Range(0, 25).Select(i => new TaskRunView("r" + i, huge, huge, huge, "2026-10-24T09:30:00Z",
                [.. Enumerable.Range(0, 40).Select(e => new TaskEventView(-1, huge, huge))]))]),
            Memory = new(true, true, null, huge, huge, huge, [.. Enumerable.Range(0, 120).Select(i => new MemoryFactView("f" + i, huge, false))], 1),
        });

        Assert.Equal(LocalNotesStore.MaxNotes, v.Notes.Items.Count);
        Assert.Equal(ExtrasViews.ErrorText, v.Notes.Error!.Length);
        Assert.All(v.Notes.Items, n => Assert.True(n.Title.Length <= LocalNotesStore.MaxTitleLength && n.Text.Length <= LocalNotesStore.MaxTextLength));
        Assert.Equal(LocalNotesStore.MaxNotes, v.Notes.Limit);

        Assert.Equal(ClipboardShelf.MaxItems, v.Clipboard.Items.Count);
        Assert.Null(v.Clipboard.NextId); // item 149 was cut, so Copy next names nothing rather than a missing row
        Assert.All(v.Clipboard.Items, c => Assert.True(c.Text.Length == ExtrasViews.ClipPreview && c.Truncated && c.Source!.Length == ExtrasViews.SourceText));
        Assert.Equal("2026-10-24T09:30:00.000Z", v.Clipboard.Items[0].AddedAt);

        Assert.Equal(TaskHistoryStore.MaxRuns, v.Tasks.Runs.Count);
        Assert.All(v.Tasks.Runs, r => Assert.True(r.Task.Length == ExtrasViews.TaskText && r.Status.Length == ExtrasViews.StatusText && r.Events.Count == ExtrasViews.TaskEvents));
        Assert.All(v.Tasks.Runs[0].Events, e => Assert.True(e.Step == 0 && e.Message.Length == ExtrasViews.TaskEventText));

        Assert.Equal(CompanionMemoryStore.MaxFacts, v.Memory.Facts.Count);
        Assert.Equal(CompanionMemoryStore.MaxNameLength, v.Memory.Name.Length);
        Assert.Equal(CompanionMemoryStore.MaxInstructionsLength, v.Memory.Instructions.Length);
        Assert.All(v.Memory.Facts, f => Assert.Equal(CompanionMemoryStore.MaxFactLength, f.Text.Length));
    }

    [Fact]
    public void BuildCarriesTheExtrasFromTheRuntimeAndTheyReachTheWire()
    {
        var notes = ExtrasViews.Notes(new(LocalNotesLoadStatus.Loaded, [new("a", "Title", "Body", At)], null));
        var v = ShellSettingsBuilder.Build(new AppSettings(), SettingsRuntime.Empty with { Notes = notes, Tasks = new(true, false, []) });
        Assert.Equal("Title", Assert.Single(v.Notes.Items).Title);
        Assert.True(v.Tasks.Active);
        Assert.Empty(v.Clipboard.Items);
        var json = JsonSerializer.Serialize(v, ShellContract.Options);
        Assert.Contains("\"notes\":{\"editable\":true,\"error\":null,\"items\":[{\"id\":\"a\"", json);
        Assert.Contains("\"memory\":{\"editable\":true,\"saved\":false", json);
    }

    // ---- Args

    [Fact]
    public void NoteArgsAddOrUpdateWithinTheStoreLimits()
    {
        Assert.True(ExtrasArgs.TryNote("""{"title":" Dentist ","text":"Thu 3pm"}""", out var id, out var title, out var text));
        Assert.Null(id);
        Assert.Equal("Dentist", title);
        Assert.Equal("Thu 3pm", text);
        Assert.True(ExtrasArgs.TryNote("""{"id":"abc","title":"T","text":""}""", out id, out _, out _));
        Assert.Equal("abc", id);
        Assert.True(ExtrasArgs.TryNote("""{"id":"","title":"T","text":"x"}""", out id, out _, out _));
        Assert.Null(id);

        foreach (var bad in new[]
        {
            null, "", "not json", "[]", """{"title":"   ","text":"x"}""", """{"text":"x"}""", """{"title":1,"text":"x"}""",
            """{"title":"T","text":{"x":1}}""", """{"id":"has space","title":"T","text":"x"}""", "{\"id\":\"" + new string('a', 65) + "\",\"title\":\"T\",\"text\":\"x\"}",
            "{\"title\":\"" + new string('t', 101) + "\",\"text\":\"x\"}", "{\"title\":\"T\",\"text\":\"" + new string('x', 10_001) + "\"}",
        })
            Assert.False(ExtrasArgs.TryNote(bad, out _, out _, out _), bad);
    }

    [Fact]
    public void MemoryArgsAreCheckedAgainstTheMemoryLimits()
    {
        Assert.True(ExtrasArgs.TryProfile("""{"name":"Clicky","personality":"","instructions":"Brief"}""", out var name, out _, out var instructions));
        Assert.Equal("Clicky", name);
        Assert.Equal("Brief", instructions);
        Assert.False(ExtrasArgs.TryProfile("{\"name\":\"" + new string('n', 81) + "\"}", out _, out _, out _));
        Assert.False(ExtrasArgs.TryProfile("{\"instructions\":\"" + new string('i', 8_001) + "\"}", out _, out _, out _));

        Assert.Equal("Likes tea", ExtrasArgs.Fact("  Likes tea "));
        Assert.Null(ExtrasArgs.Fact("   "));
        Assert.Null(ExtrasArgs.Fact(new string('f', 1_001)));

        Assert.True(ExtrasArgs.TryIdText("""{"id":"f1","text":"New"}""", CompanionMemoryStore.MaxFactLength, out var id, out var text));
        Assert.Equal(("f1", "New"), (id, text));
        Assert.False(ExtrasArgs.TryIdText("""{"id":"f1","text":"  "}""", 1_000, out _, out _));
        Assert.False(ExtrasArgs.TryIdText("""{"text":"x"}""", 1_000, out _, out _));
        Assert.False(ExtrasArgs.TryIdText("{\"id\":\"f1\",\"text\":\"" + new string('x', 1_001) + "\"}", 1_000, out _, out _));
    }

    [Fact]
    public void ClipboardArgsTakeOnlyShelfIdsAndBoundedText()
    {
        Assert.Equal(12L, ExtrasArgs.ClipId("12"));
        foreach (var bad in new[] { null, "", "0", "-1", "+3", " 3", "3.0", "abc", "99999999999999999999" }) Assert.Null(ExtrasArgs.ClipId(bad));
        Assert.Equal("copied", ExtrasArgs.ClipText("copied"));
        Assert.Null(ExtrasArgs.ClipText(" \n "));
        Assert.Null(ExtrasArgs.ClipText(new string('c', ExtrasArgs.MaxClipText + 1)));
    }

    // ---- Intents

    private static string Action(string action, string? arg) =>
        JsonSerializer.Serialize(arg is null ? new Dictionary<string, string> { ["kind"] = "settings.action", ["action"] = action }
            : new Dictionary<string, string> { ["kind"] = "settings.action", ["action"] = action, ["arg"] = arg });

    [Theory]
    [InlineData("note.save"), InlineData("memory.profile"), InlineData("memory.add"), InlineData("memory.update"), InlineData("clipboard.add"), InlineData("clipboard.edit")]
    public void TextActionsTakeLongArgsUpToTheirCap(string action)
    {
        Assert.True(ShellIntentParser.TryParse(Action(action, new string('a', ShellIntentParser.MaxTextArgLength)), out _));
        Assert.False(ShellIntentParser.TryParse(Action(action, new string('a', ShellIntentParser.MaxTextArgLength + 1)), out _));
    }

    [Theory]
    [InlineData("note.delete"), InlineData("clipboard.copy"), InlineData("clipboard.delete"), InlineData("task.export"), InlineData("memory.delete")]
    public void IdActionsKeepTheShortArgCap(string action)
    {
        Assert.True(ShellIntentParser.TryParse(Action(action, "12"), out _));
        Assert.False(ShellIntentParser.TryParse(Action(action, new string('1', 513)), out _));
    }

    [Fact]
    public void UnknownExtrasActionsAreRefused()
    {
        foreach (var action in new[] { "note.wipe", "clipboard.paste", "task.run", "memory.export", "notes.save" })
            Assert.False(ShellIntentParser.TryParse(Action(action, null), out _), action);
    }
}
