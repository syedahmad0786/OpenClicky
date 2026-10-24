using System.Security.Cryptography;

namespace OpenClicky.App;

internal static class LocalWhisperModel
{
    internal const long Size = 147964211;
    internal const string Sha256 = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";
    internal static string PathOnDisk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "models", "ggml-base.en.bin");
    internal static bool IsAvailable => File.Exists(PathOnDisk) && new FileInfo(PathOnDisk).Length == Size;

    internal static async Task VerifyAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != Size) throw new InvalidDataException("Download the accurate voice model in Settings → Voice & shortcut.");
        await using var file = File.OpenRead(path);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The voice model failed its integrity check. Download it again in Settings.");
    }

    internal static async Task DownloadAsync(IProgress<int>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        var temporary = PathOnDisk + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin", HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    downloaded += read;
                    if (downloaded > Size) throw new InvalidDataException("The voice-model download was larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report((int)(downloaded * 100 / Size));
                }
            }
            await VerifyAsync(temporary, token);
            File.Move(temporary, PathOnDisk, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
