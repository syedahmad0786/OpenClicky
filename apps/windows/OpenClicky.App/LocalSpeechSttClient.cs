using System.Globalization;
using System.Speech.Recognition;
using OpenClicky.Core;

namespace OpenClicky.App;

/// <summary>Uses an installed Windows speech language; audio stays on this PC.</summary>
public sealed class LocalSpeechSttClient : ISttClient
{

    private static SpeechRecognitionEngine CreateEngine()
    {
        var installed = SpeechRecognitionEngine.InstalledRecognizers();
        var info = installed.FirstOrDefault(r => r.Culture.Equals(CultureInfo.CurrentUICulture))
            ?? installed.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en")
            ?? installed.FirstOrDefault()
            ?? throw new InvalidOperationException("No Windows speech language is installed. Type your request instead.");
        var engine = new SpeechRecognitionEngine(info);
        engine.LoadGrammar(new DictationGrammar());
        return engine;
    }

    public static string[] InstalledLanguages() => SpeechRecognitionEngine.InstalledRecognizers()
        .Select(r => r.Culture.DisplayName).Distinct().ToArray();

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (wavBytes.Length <= 44) return string.Empty;
        using var engine = CreateEngine();
        using var stream = new MemoryStream(wavBytes, writable: false);
        engine.SetInputToWaveStream(stream);
        var words = new List<string>();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SpeechRecognized += (_, e) => words.Add(e.Result.Text);
        engine.RecognizeCompleted += (_, e) =>
        {
            if (e.Error is not null) completion.TrySetException(e.Error);
            else if (e.Cancelled || cancellationToken.IsCancellationRequested) completion.TrySetCanceled(cancellationToken);
            else completion.TrySetResult(string.Join(" ", words));
        };
        engine.RecognizeAsync(RecognizeMode.Multiple);
        using var registration = cancellationToken.Register(engine.RecognizeAsyncCancel);
        return await completion.Task.ConfigureAwait(false);
    }
}
