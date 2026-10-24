using OpenClicky.App;
using OpenClicky.Core;

internal static class DictationPanelCheck
{
    internal static int Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new Form();
                using var panel = new DictationPanel(() => Task.CompletedTask, _ => Task.CompletedTask, () => { });
                host.Controls.Add(panel);
                panel.Begin("Test app", "Test field");
                var controls = Descendants(panel).ToArray();
                var raw = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Raw dictation transcript");
                var draft = controls.OfType<TextBox>().Single(c => c.AccessibleName == "Editable dictation draft");
                var status = controls.OfType<Label>().Single(c => c.AccessibleName == "Dictation status");
                var insert = controls.OfType<Button>().Single(c => c.Text == "Insert into original app");
                var record = controls.OfType<Button>().Single(c => c.Text == "Start recording");

                panel.SetVoiceState(CompanionVoiceState.Listening);
                panel.ShowLive("Please keep this");
                Check(raw.Text == "Please keep this" && draft.Text == raw.Text, "Live words did not fill both preview fields.");
                Check(!insert.Enabled && record.Enabled, "Insertion must be disabled while recording.");
                draft.Text = "My reviewed draft";
                panel.ShowLive("Please keep this as a draft");
                Check(raw.Text == "Please keep this as a draft" && draft.Text == "My reviewed draft", "Live recognition overwrote an edit.");
                panel.SetVoiceState(CompanionVoiceState.Processing);
                Check(!insert.Enabled && !record.Enabled, "Finishing must disable both actions.");
                panel.AcceptTranscript("Please keep this as a draft.");
                panel.ShowLive("");
                panel.SetVoiceState(CompanionVoiceState.Idle);
                Check(status.Text.StartsWith("Review or edit") && insert.Enabled, "Idle/empty preview erased the final result.");
                panel.SetResult("Microphone unavailable");
                Check(status.Text == "Microphone unavailable", "Microphone error is not inline.");
                panel.AcceptTranscript("");
                Check(status.Text.StartsWith("No words were recognized") && draft.Text == "My reviewed draft", "Empty recognition lacks feedback or destroyed an edit.");
                panel.SetResult("Inserted and verified", canContinue: false);
                panel.SetVoiceState(CompanionVoiceState.Idle);
                Check(!insert.Enabled && !record.Enabled, "A consumed destination re-enabled input.");
                panel.End();
                panel.Begin("New app", "New field");
                Check(raw.Text == "" && draft.Text == "" && record.Enabled, "A new session did not reset correctly.");
                Console.WriteLine("PASS dictation live preview, edited draft preservation, inline empty/error feedback, consumed-target controls and new-session reset");
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is null) return 0;
        Console.Error.WriteLine(failure);
        return 1;
    }

    private static IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
        .SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
