using OpenClicky.App.Shell;
using OpenClicky.Core;
using OpenClicky.Core.CompanionCursor;
using OpenClicky.Core.Drawing;
using OpenClicky.Core.Shell;
using OpenClicky.Overlay;

namespace OpenClicky.App;

/// <summary>Saved drawings: the save offer after a turn that drew, the rows in Chat, replay in three modes, print and remove.</summary>
internal sealed partial class TrayApplicationContext
{
    private readonly DrawingStore _drawings = new();
    /// <summary>The saved list the shell shows. Read from disk once and again only after a save or remove (every state push asks for it).</summary>
    private IReadOnlyList<(string Id, string Title, DateTimeOffset CreatedAt)>? _savedDrawings;
    private DrawingReplayWindow? _replay;

    /// <summary>UI thread, once at startup.</summary>
    private void WireDrawings() => _talk.DrawingTurnCompleted += _ => OnUi(OfferToSaveDrawing);

    /// <summary>The Chat rows' source: a store that cannot be read is an empty list, never a failed push.</summary>
    private IReadOnlyList<(string Id, string Title, DateTimeOffset CreatedAt)> SavedDrawings() =>
        _savedDrawings ??= ShellSource("drawings", _drawings.List, []);

    /// <summary>UI thread: a turn that drew has ended with its drawing still on screen. Dismissing the card leaves it there, unsaved.</summary>
    private void OfferToSaveDrawing()
    {
        if (_shell is { IsActive: true } shell && _overlay.HasDrawing) shell.AddCard(CompanionCards.SaveDrawing());
    }

    /// <summary>UI thread: the card's Save.</summary>
    private void SaveLastDrawing()
    {
        if (_talk.LastDrawingTurn is not { } drawing)
        {
            _shell?.AddToast("That drawing is already gone, so there is nothing to keep.");
            return;
        }
        try
        {
            _drawings.Save(drawing);
            _savedDrawings = null;
            _shell?.AddToast("Saved. You'll find it in Chat.");
            RequestShellPush();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log("drawing_save_failed=" + ex.GetType().Name);
            _shell?.AddToast(ex is InvalidDataException ? "This drawing is too big to keep." : "I couldn't save the drawing.");
        }
    }

    /// <summary>UI thread: a button on a drawing row. The id was checked against the pushed state; the action is allow-listed.</summary>
    void IShellHost.DrawingAction(string id, string action)
    {
        if (action == "remove")
        {
            if (_drawings.Delete(id)) { _savedDrawings = null; RequestShellPush(); }
            else _shell?.AddToast("I couldn't remove that drawing.");
            return;
        }
        if (_drawings.Load(id) is not { } drawing)
        {
            _savedDrawings = null; // the file is gone or no longer valid: drop its row
            RequestShellPush();
            _shell?.AddToast("I couldn't open that drawing.");
            return;
        }
        switch (action)
        {
            case "replayWindow": Replay(drawing, ReplayMode.Window); break;
            case "replayFullScreen": Replay(drawing, ReplayMode.FullScreen); break;
            case "replayBoard": Replay(drawing, ReplayMode.Board); break;
            case "print": PrintDrawing(drawing); break;
        }
    }

    private void Replay(SavedDrawing drawing, ReplayMode mode)
    {
        if (_taskCts is not null || _talk.State != CompanionVoiceState.Idle)
        {
            _shell?.AddToast("Finish what I'm doing first, then replay it.");
            return;
        }
        CloseReplay();
        _talk.ClearDrawing(); // the live drawing is not drawn under the replay
        var style = CompanionStyle.From(_settings, GeneralBehaviour.ReducedMotion());
        Func<string, CancellationToken, Task>? speak = _settings.SpeakReplies ? (text, token) => _tts.SpeakAsync(text, token) : null;
        DrawingReplayWindow replay;
        try
        {
            replay = new DrawingReplayWindow(drawing, mode,
                new ReplayOptions(System.Drawing.ColorTranslator.FromHtml(style.AccentHex), style.ReducedMotion, !_settings.ShowInScreenRecordings, speak, Log));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("drawing_replay_failed=" + ex.GetType().Name);
            _shell?.AddToast("I couldn't replay that drawing.");
            return;
        }
        _replay = replay;
        replay.Closed += () =>
        {
            if (ReferenceEquals(_replay, replay)) _replay = null;
            _tts.Stop();
        };
    }

    /// <summary>UI thread: closes an open replay (Esc, Stop, a new turn or task, quitting).</summary>
    private void CloseReplay() => _replay?.Close();

    /// <summary>The user asked for the print dialog; nothing is sent to a printer until they choose one and confirm.</summary>
    private void PrintDrawing(SavedDrawing drawing)
    {
        try
        {
            var style = CompanionStyle.From(_settings, reducedMotion: false);
            DrawingPrinter.Print(null, drawing, System.Drawing.ColorTranslator.FromHtml(style.AccentHex));
        }
        catch (Exception ex) when (ex is System.Drawing.Printing.InvalidPrinterException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log("drawing_print_failed=" + ex.GetType().Name);
            _shell?.AddToast("I couldn't print that. Check the printer and try again.");
        }
    }
}
