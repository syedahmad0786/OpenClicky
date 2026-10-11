using System.Text;
using OpenClicky.App;
using OpenClicky.Core;
using System.Speech.Synthesis;

if (args.Contains("--speech")) return await CheckSpeechAsync();

// Local smoke: CredWrite for small secrets + DPAPI fallback for OAuth-sized blobs.
Console.WriteLine("OpenClicky secret-store smoke starting…");

var store = new WindowsCredentialSecretStore();
const string smallName = "_smoke-small";
const string largeName = "_smoke-large-oauth";
const string namedSmoke = "chatgpt-oauth-smoke";

try
{
    store.Delete(smallName);
    store.Delete(largeName);
    store.Delete(namedSmoke);

    store.Set(smallName, "openclicky-small-secret");
    var smallRoundTrip = store.Get(smallName);
    if (smallRoundTrip != "openclicky-small-secret")
    {
        Fail($"small round-trip mismatch: '{smallRoundTrip}'");
    }

    Console.WriteLine("OK small CredWrite/CredRead round-trip");

    // Mimic ChatGPT OAuth JSON (three JWT-sized fields) — exceeds 2560-byte CredMan blob limit.
    var large = "{\"email\":\"smoke@example.com\",\"accessToken\":\"ACCESS_"
        + new string('A', 1800)
        + "\",\"refreshToken\":\"REFRESH_"
        + new string('B', 1800)
        + "\",\"idToken\":\"ID_"
        + new string('C', 1800)
        + "\",\"clientId\":\"client_smoke\",\"scopes\":[\"chatgpt.tokens.use.direct\"]}";

    var unicodeBytes = Encoding.Unicode.GetByteCount(large);
    Console.WriteLine($"large_payload unicode_bytes={unicodeBytes} utf8_bytes={Encoding.UTF8.GetByteCount(large)}");
    if (unicodeBytes <= 2560)
    {
        Fail("test payload too small to exercise DPAPI fallback");
    }

    store.Set(largeName, large);
    var largeRoundTrip = store.Get(largeName);
    if (largeRoundTrip != large)
    {
        Fail("large OAuth-sized round-trip mismatch (DPAPI path)");
    }

    var dpapiPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenClicky",
        "secrets",
        largeName + ".dpapi");
    if (!File.Exists(dpapiPath))
    {
        Fail($"expected DPAPI file missing: {dpapiPath}");
    }

    Console.WriteLine($"OK large DPAPI round-trip file={dpapiPath}");

    store.Set(namedSmoke, large);
    if (store.Get(namedSmoke) != large)
    {
        Fail("named oauth-smoke round-trip failed");
    }

    store.Delete(namedSmoke);
    store.Delete(smallName);
    store.Delete(largeName);

    if (store.Get(smallName) is not null || store.Get(largeName) is not null || store.Get(namedSmoke) is not null)
    {
        Fail("delete did not clear secrets");
    }

    Console.WriteLine("OpenClicky secret-store smoke PASSED");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    try
    {
        store.Delete(smallName);
        store.Delete(largeName);
        store.Delete(namedSmoke);
    }
    catch
    {
        // ignore cleanup errors
    }

    return 1;
}

static void Fail(string message)
{
    throw new InvalidOperationException(message);
}

static async Task<int> CheckSpeechAsync()
{
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (LocalSpeechSttClient.InstalledLanguages().Length == 0) Fail("No installed Windows speech language.");
        using var wav = new MemoryStream();
        using (var voice = new SpeechSynthesizer())
        {
            voice.SetOutputToWaveStream(wav);
            voice.Speak("Hello there. Please open the window.");
        }
        var transcript = await new LocalSpeechSttClient().TranscribeAsync(wav.ToArray(), timeout.Token);
        if (!transcript.Contains("window", StringComparison.OrdinalIgnoreCase)) Fail("Windows recognition did not recognize the fixture sentence: " + transcript);
        Console.WriteLine("PASS local Windows recognition from synthesized WAV: " + transcript);
        var tts = new LocalSpeechTtsClient { Enabled = false };
        await tts.SpeakAsync("This should remain silent.");
        tts.Enabled = true;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await tts.SpeakAsync(string.Join(" ", Enumerable.Repeat("Testing cancellation of a spoken reply.", 30)), cancel.Token).WaitAsync(TimeSpan.FromSeconds(5));
            Fail("Speech was not cancelled.");
        }
        catch (OperationCanceledException) { }
        if (clock.Elapsed > TimeSpan.FromSeconds(5)) Fail("Speech cancellation took too long.");
        Console.WriteLine("PASS real Windows TTS cancellation in " + clock.ElapsedMilliseconds + "ms");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
