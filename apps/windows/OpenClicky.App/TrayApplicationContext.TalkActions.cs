using System.Diagnostics;
using OpenClicky.Agent;
using OpenClicky.Core;
using OpenClicky.Core.Shell;
using OpenClicky.Core.Work;

namespace OpenClicky.App;

/// <summary>Runs the [OPEN]/[COPY]/[TYPE] actions in a talk reply under the access mode the reply arrived with.</summary>
internal sealed partial class TrayApplicationContext
{
    /// <summary>UI thread: the last talk copy, for its Undo.</summary>
    private (string? Previous, string Copied)? _talkCopy;

    /// <summary>UI thread: the clipboard text from before a reply first changed it (muted-speaker copy or a COPY tag).</summary>
    private sealed class ReplyClipboard
    {
        public bool Captured;
        public string? Original;

        public void CaptureOnce()
        {
            if (Captured) return;
            Original = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
            Captured = true;
        }
    }

    /// <summary>UI thread: replaced when a turn starts processing.</summary>
    private ReplyClipboard _replyClipboard = new();

    private void WireTalkActions()
    {
        // The orchestrator snapshots the mode when the reply arrives; a later change can only narrow it.
        _talk.ActionsRequested += (actions, replyAccess) =>
        {
            var mode = TalkActionPolicy.EffectiveMode(replyAccess, _settings.AgentAccess);
            _uiContext.Post(_ => _ = RunTalkActionsAsync(actions, mode), null);
        };
    }

    /// <summary>UI thread, one action at a time. A failed action shows a notice and never reaches the tray.</summary>
    private async Task RunTalkActionsAsync(IReadOnlyList<TalkAction> actions, AgentAccessMode mode)
    {
        var clipboard = _replyClipboard; // posted in order with the turn's state changes: still this reply's
        foreach (var action in actions)
        {
            try
            {
                // Checked per action, on the UI thread: a foreground run that is not paused may be typing too.
                var taskRunning = ForegroundMayAct;
                var decision = TalkActionPolicy.Decide(action, mode, taskRunning);
                Log($"talk_action kind={action.Kind} decision={decision} mode={mode} task_running={taskRunning}");
                switch (decision)
                {
                    case TalkActionDecision.PreviewOnly:
                        ShowTalkActionCard(mode != AgentAccessMode.Preview && action.Kind == TalkActionKind.Type
                            ? TalkActionText.TypeHeldForTask(action) : TalkActionText.Preview(action));
                        break;
                    case TalkActionDecision.AskFirst when action.Kind == TalkActionKind.Type:
                        await TypeTalkTextAsync(action, ask: true);
                        break;
                    case TalkActionDecision.AskFirst:
                        if (await _confirm.ConfirmTalkAsync(TalkActionText.ConfirmSummary(action), restoreTarget: false, _lifetime.Token)) RunTalkAction(action, asked: true, clipboard);
                        break;
                    default:
                        if (action.Kind == TalkActionKind.Type) await TypeTalkTextAsync(action, ask: false);
                        else RunTalkAction(action, asked: false, clipboard);
                        break;
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log($"talk_action_error kind={action.Kind} error={ex.GetType().Name}");
                TalkActionNotice(action.Kind switch
                {
                    TalkActionKind.Open => "I couldn't open that page. ",
                    TalkActionKind.Copy => "I couldn't copy that. ",
                    _ => "I couldn't type that. ",
                } + ex.Message);
            }
        }
    }

    private void RunTalkAction(TalkAction action, bool asked, ReplyClipboard clipboard)
    {
        if (action.Kind == TalkActionKind.Copy) { CopyTalkText(action.Value, clipboard); return; }
        // The parser validated it; check again right before handing it to the shell.
        if (!Uri.TryCreate(action.Value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
        {
            TalkActionNotice("I didn't open that address because it isn't a plain web page.");
            return;
        }
        using (Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })) { }
        if (!asked) ShowTalkActionCard(TalkActionText.Opened(uri.AbsoluteUri));
    }

    /// <summary>Undo puts back what the user had before this reply touched the clipboard, not an earlier COPY of the same reply.</summary>
    private void CopyTalkText(string text, ReplyClipboard clipboard)
    {
        clipboard.CaptureOnce();
        Clipboard.SetText(text, TextDataFormat.UnicodeText);
        _talkCopy = (clipboard.Original, text);
        if (_shell is { IsActive: true }) _shell.AddCard(TalkActionText.Copied(text));
        else _home.AddMessage("OpenClicky", TalkActionText.Copied(text).Title);
    }

    /// <summary>Copy card Undo: restore the earlier text, unless something newer is on the clipboard (then it is the user's).</summary>
    private void UndoTalkCopy()
    {
        if (_talkCopy is not { } copy) return;
        _talkCopy = null;
        try
        {
            if (!TalkActionText.CanUndoCopy(Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null, copy.Copied))
            {
                _shell?.AddToast("The clipboard has changed since then, so I left it alone.");
                return;
            }
            if (string.IsNullOrEmpty(copy.Previous)) Clipboard.Clear();
            else Clipboard.SetText(copy.Previous, TextDataFormat.UnicodeText);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log("talk_copy_undo_error=" + ex.GetType().Name);
            _shell?.AddToast("I couldn't restore the clipboard.");
        }
    }

    /// <summary>
    /// Types into the field focused now through the verified dictation insertion, which refuses password fields, deny-listed apps
    /// and OpenClicky's own windows. When asking, the dialog names that field and the text goes in only if the same field is
    /// focused again once the dialog has closed and focus returned.
    /// </summary>
    private async Task TypeTalkTextAsync(TalkAction action, bool ask)
    {
        // Our notch's fields live in the WebView2 process, so the field's process alone cannot prove the window is not ours.
        if (ForegroundWindowInfo.IsCurrentProcessForeground())
        {
            TalkActionNotice("I didn't type it. Click the field you want in another app, then ask again.");
            return;
        }
        var exclusions = AgentDenyList.ParseExtraFragments(_settings.AgentDenyListExtra);
        if (!DictationTarget.TryCapture(out var target, out var error, exclusions) || target is null)
        {
            TalkActionNotice(TalkActionText.TypeRefused(error));
            return;
        }
        if (ask && !await _confirm.ConfirmTalkAsync(TalkActionText.ConfirmSummary(action, $"{target.ControlName} in {target.AppTitle}"), restoreTarget: true, _lifetime.Token))
            return;
        if (!await WaitForModifiersReleasedAsync(_lifetime.Token))
        {
            TalkActionNotice("I didn't type it because keys were still held down.");
            return;
        }
        // A task may have started or resumed while the dialog was open or keys were held: it may be typing too.
        if (ForegroundMayAct)
        {
            ShowTalkActionCard(TalkActionText.TypeHeldForTask(action));
            return;
        }
        var text = TalkActionText.SanitizeForTyping(action.Value);
        // The screen is held while the text goes in, so a background job's input waits instead of interleaving with it.
        using var screen = await _lease.TryAcquireAsync(DesktopLease.TalkHolder, DesktopLease.TalkWait, _lifetime.Token);
        if (screen is null)
        {
            ShowTalkActionCard(TalkActionText.TypeHeldForTask(action));
            return;
        }
        var result = await target.InsertAsync(text, _executor, _lifetime.Token, requireCurrentFocus: true, exclusions: exclusions);
        Log($"talk_type attempted={result.InputAttempted} verified={result.Verified}");
        // The insertion's own messages are worded for dictation drafts.
        TalkActionNotice(TalkActionText.TypeResult(result.InputAttempted, result.InputCompleted, result.Verified, result.FinalText is not null, text.Length));
    }

    private void ShowTalkActionCard(NotchCard card)
    {
        if (_shell is { IsActive: true }) _shell.AddCard(card);
        else _home.AddMessage("OpenClicky", card.Body is null ? card.Title : card.Title + "\r\n" + card.Body);
    }

    private void TalkActionNotice(string text)
    {
        if (_shell is { IsActive: true }) _shell.AddToast(text);
        else _home.AddMessage("OpenClicky", text);
    }
}
