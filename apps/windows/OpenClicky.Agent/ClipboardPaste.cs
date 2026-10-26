using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenClicky.Agent;

/// <summary>
/// Puts text into the focused field with one Ctrl+V. Per-character Unicode input can be read out of order by a busy target
/// (Notepad turned "five six" into "vvve xxx"), and rich web editors (Slack, Gmail) take a paste as one edit. The text goes on
/// the clipboard marked private (no clipboard history, no cloud clipboard, skipped by clipboard monitors, OpenClicky's included),
/// and what was there before is put back once the text has landed.
/// </summary>
internal static class ClipboardPaste
{
    /// <summary>Null when the clipboard could not be taken (nothing was sent); otherwise the Ctrl+V result.</summary>
    public static async Task<AgentActionResult?> PasteAsync(string text, IAgentActionExecutor executor, Func<bool> landed,
        CancellationToken cancellationToken)
    {
        var saved = await OnSta(Save).ConfigureAwait(false);
        uint ours;
        try { ours = await OnSta(() => Put(text)).ConfigureAwait(false); }
        catch (ExternalException) { return null; }
        try
        {
            var result = await executor.ExecuteAsync(new AgentActionRequest(AgentToolNames.Hotkey,
                new Dictionary<string, string> { ["keys"] = "ctrl+v" }, "Paste the dictation into the selected field"),
                userConfirmed: true, cancellationToken).ConfigureAwait(false);
            // The target reads the clipboard while it handles the keys: wait for the text to show (at most 800 ms) before restoring.
            for (var waited = 0; result.Success && waited < 800 && !landed(); waited += 40) await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            // Only while the clipboard still holds our copy: anything newer belongs to the user.
            await OnSta(() => { if (GetClipboardSequenceNumber() == ours) Restore(saved); return 0; }).ConfigureAwait(false);
        }
    }

    private static uint Put(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        Clipboard.SetDataObject(data, copy: true, retryTimes: 5, retryDelay: 40);
        return GetClipboardSequenceNumber();
    }

    /// <summary>
    /// A copy of every format the clipboard will hand over now; null when it was empty or could not be read.
    /// ponytail: formats an app renders only on request and cannot render any more are lost; a full IDataObject proxy if that matters.
    /// </summary>
    private static DataObject? Save()
    {
        try
        {
            if (Clipboard.GetDataObject() is not { } current) return null;
            var copy = new DataObject();
            foreach (var format in current.GetFormats(autoConvert: false))
            {
                try { if (current.GetData(format, autoConvert: false) is { } data) copy.SetData(format, autoConvert: false, data); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { }
            }
            return copy.GetFormats().Length == 0 ? null : copy;
        }
        catch (ExternalException) { return null; }
    }

    private static void Restore(DataObject? saved)
    {
        try
        {
            if (saved is null) Clipboard.Clear();
            else Clipboard.SetDataObject(saved, copy: true, retryTimes: 5, retryDelay: 40);
        }
        catch (ExternalException) { }
    }

    private static Task<T> OnSta<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(work()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "OpenClicky paste" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
