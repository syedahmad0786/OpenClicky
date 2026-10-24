using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenClicky.App;
using OpenClicky.Core;
using OpenClicky.Providers;

internal static class ChatGptLatencyCheck
{
    private const string RequiredModel = "gpt-6-luna";

    public static async Task<int> RunAsync(string? outputPath, bool productionPath = false)
    {
        var artifactName = productionPath ? "chatgpt-latency-production" : "chatgpt-latency";
        outputPath ??= Path.Combine("artifacts", $"{artifactName}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        var settings = SettingsStore.Load();
        var secrets = new WindowsCredentialSecretStore();
        var model = settings.ResolveTalkModel();
        if (settings.AuthProvider != AuthProvider.ChatGPT || !ProviderFactory.IsChatGptSignedIn(secrets))
            throw new InvalidOperationException("The existing ChatGPT plan session is unavailable. No sign-in was started.");
        if (!string.Equals(model, RequiredModel, StringComparison.Ordinal))
            throw new InvalidOperationException($"Latency smoke requires the selected Talk model to remain {RequiredModel}; current selection is '{model}'.");

        var oauth = new ChatGptOAuthService();
        var handler = new ReasoningInjectionHandler(new HttpClientHandler());
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        var client = new ChatGptTalkClient(ct => oauth.GetAccessTokenAsync(settings, secrets, ct), http);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));

        handler.Effort = null;
        _ = await client.CompleteVisionTalkAsync(
            "Return only the word READY.", [], "Synthetic latency warmup. Return READY.", [], model, timeout.Token);
        handler.UseProductionPath = productionPath;

        var fixture = FixtureScreen();
        var runs = new List<object>();
        var schedule = productionPath
            ? new (string Case, string Effort)[] { ("chart", "production-low"), ("diagram", "production-low"), ("screen", "production-low") }
            : [("chart", "default"), ("chart", "low"), ("chart", "none"),
                ("diagram", "low"), ("diagram", "none"), ("diagram", "default"),
                ("screen", "none"), ("screen", "default"), ("screen", "low")];

        foreach (var (caseName, effort) in schedule)
        {
            handler.Effort = effort is "default" or "production-low" ? null : effort;
            var watch = Stopwatch.StartNew();
            long? firstTextMs = null;
            var (instructions, prompt, screens) = Request(caseName, fixture);
            var output = await client.StreamVisionTalkAsync(instructions, [], prompt, screens, model,
                _ => firstTextMs ??= watch.ElapsedMilliseconds, timeout.Token);
            watch.Stop();
            var valid = Validate(caseName, output);
            runs.Add(new { @case = caseName, effort, firstTextMs, totalMs = watch.ElapsedMilliseconds, valid });
            Console.WriteLine(JsonSerializer.Serialize(new { @case = caseName, effort, firstTextMs, totalMs = watch.ElapsedMilliseconds, valid }));
        }

        var artifact = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            model,
            productionPath,
            productionLowRequests = handler.ProductionLowRequests,
            warmupCalls = 1,
            measuredCalls = runs.Count,
            syntheticInputsOnly = true,
            note = "One call per case/effort; directional latency sample, not a stable benchmark.",
            runs,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(artifact, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("artifact=" + outputPath);
        return 0;
    }

    private static (string Instructions, string Prompt, IReadOnlyList<ScreenCaptureResult> Screens) Request(
        string caseName, ScreenCaptureResult fixture) => caseName switch
    {
        "chart" => ("Return strict JSON only, with no Markdown.",
            "Create a chart artifact exactly as {\"kind\":\"chart\",\"title\":\"Synthetic plan\",\"series\":[{\"label\":\"Plan\",\"value\":12},{\"label\":\"Build\",\"value\":20},{\"label\":\"Verify\",\"value\":8}]}. Preserve every value.", []),
        "diagram" => ("Return strict JSON only, with no Markdown.",
            "Create a labelled diagram exactly as {\"kind\":\"diagram\",\"title\":\"Synthetic flow\",\"nodes\":[{\"id\":\"draft\",\"label\":\"Draft\"},{\"id\":\"review\",\"label\":\"Review\"},{\"id\":\"done\",\"label\":\"Done\"}],\"edges\":[{\"from\":\"draft\",\"to\":\"review\"},{\"from\":\"review\",\"to\":\"done\"}]}. Preserve labels and edges.", []),
        _ => ("Inspect only the supplied synthetic image. Return strict JSON only: {\"summary\":string,\"controls\":string[]}. List every visible control/text item and invent none.",
            "Explain this generated harmless test screen.", [fixture]),
    };

    private static bool Validate(string caseName, string output)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(output));
            var root = document.RootElement;
            if (caseName == "chart")
            {
                var values = root.GetProperty("series").EnumerateArray()
                    .ToDictionary(item => item.GetProperty("label").GetString()!, item => item.GetProperty("value").GetInt32());
                return root.GetProperty("kind").GetString() == "chart" && values.Count == 3
                    && values.GetValueOrDefault("Plan") == 12 && values.GetValueOrDefault("Build") == 20 && values.GetValueOrDefault("Verify") == 8;
            }
            if (caseName == "diagram")
            {
                var labels = root.GetProperty("nodes").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
                var edges = root.GetProperty("edges").EnumerateArray().Select(item =>
                    item.GetProperty("from").GetString() + ">" + item.GetProperty("to").GetString()).ToArray();
                return root.GetProperty("kind").GetString() == "diagram"
                    && labels.SequenceEqual(["Draft", "Review", "Done"])
                    && edges.SequenceEqual(["draft>review", "review>done"]);
            }
            var controls = root.GetProperty("controls").EnumerateArray().Select(item => item.GetString()?.Trim().ToLowerInvariant()).ToArray();
            return controls.Length == 2 && controls.Any(item => item?.Contains("green button") == true)
                && controls.Any(item => item?.Contains("status: ready") == true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start) throw new JsonException("No JSON object.");
        return text[start..(end + 1)];
    }

    private static ScreenCaptureResult FixtureScreen()
    {
        using var bitmap = new Bitmap(640, 360);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var button = new SolidBrush(Color.SeaGreen);
            graphics.FillRectangle(button, 50, 65, 210, 70);
            using var buttonFont = new Font("Segoe UI", 18, FontStyle.Bold);
            using var textFont = new Font("Segoe UI", 18);
            graphics.DrawString("Green button", buttonFont, Brushes.White, 72, 84);
            graphics.DrawString("Status: Ready", textFont, Brushes.Black, 50, 190);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return new(stream.ToArray(), 640, 360, 0, 0, 640, 360, true, "synthetic generated screen");
    }

    private sealed class ReasoningInjectionHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public string? Effort { get; set; }
        public bool UseProductionPath { get; set; }
        public int ProductionLowRequests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/v1/responses")
            {
                var json = await request.Content!.ReadAsStringAsync(token).ConfigureAwait(false);
                var payload = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Benchmark request was invalid.");
                if (UseProductionPath)
                {
                    if (payload["reasoning"]?["effort"]?.GetValue<string>() != "low")
                        throw new InvalidDataException("Production latency request did not use low reasoning.");
                    ProductionLowRequests++;
                }
                else if (Effort is null)
                {
                    payload.Remove("reasoning");
                }
                else
                {
                    payload["reasoning"] = new JsonObject { ["effort"] = Effort };
                }
                request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8);
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            }
            return await base.SendAsync(request, token).ConfigureAwait(false);
        }
    }
}
