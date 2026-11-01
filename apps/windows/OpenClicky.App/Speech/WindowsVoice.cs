using System.Speech.Synthesis;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Speech;

namespace OpenClicky.App.Speech;

/// <summary>Windows (SAPI) voices rendered into memory, with word timings from SpeakProgress.</summary>
internal static class WindowsVoice
{
    /// <param name="voiceId">"windows-default" or a short id from <see cref="SettingsActionArgs.VoiceShortId"/>; an id that is not installed uses the default voice.</param>
    public static async Task<SpeechClip> RenderAsync(string text, string? voiceId, double speed, CancellationToken cancellationToken)
    {
        using var wav = new MemoryStream();
        var words = new List<(string, TimeSpan)>();
        using (var synth = new SpeechSynthesizer())
        {
            if (voiceId is not null && voiceId != SettingsCatalog.WindowsDefaultVoice
                && synth.GetInstalledVoices().FirstOrDefault(v => v.Enabled && SettingsActionArgs.VoiceShortId(v.VoiceInfo.Id) == voiceId) is { } voice)
                synth.SelectVoice(voice.VoiceInfo.Name);
            synth.Rate = SpeechPlan.SapiRate(speed);
            synth.SetOutputToWaveStream(wav);
            // SAPI delivers progress and completion in order through one serialized worker: the list is complete at Completed.
            synth.SpeakProgress += (_, e) => words.Add((e.Text, e.AudioPosition));
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            synth.SpeakCompleted += (_, e) =>
            {
                if (e.Cancelled) done.TrySetCanceled();
                else if (e.Error is not null) done.TrySetException(e.Error);
                else done.TrySetResult();
            };
            synth.SpeakAsync(text);
            using (cancellationToken.Register(synth.SpeakAsyncCancelAll)) await done.Task.ConfigureAwait(false);
            synth.SetOutputToNull();
        }
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = wav.ToArray();
        var info = SpeechPlan.ReadWav(bytes) ?? throw new InvalidDataException("Windows speech produced no audio.");
        return new SpeechClip(bytes, info, words);
    }
}
