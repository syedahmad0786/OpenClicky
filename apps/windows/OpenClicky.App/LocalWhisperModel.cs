using System.Security.Cryptography;
using OpenClicky.Core.Dictation;

namespace OpenClicky.App;

internal static class LocalWhisperModel
{
    internal static string PathOnDisk(WhisperModelInfo model) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenClicky", "models", model.FileName);

    internal static bool IsAvailable(WhisperModelInfo model)
    {
        var path = PathOnDisk(model);
        return File.Exists(path) && new FileInfo(path).Length == model.Size;
    }

    internal static async Task VerifyAsync(WhisperModelInfo model, string path, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != model.Size) throw new InvalidDataException("Download the accurate voice model in Settings → Voice & shortcut.");
        await using var file = File.OpenRead(path);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The voice model failed its integrity check. Download it again in Settings.");
    }

    internal static async Task DownloadAsync(WhisperModelInfo model, IProgress<int>? progress = null, CancellationToken token = default)
    {
        var destination = PathOnDisk(model);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, token);
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
                    if (downloaded > model.Size) throw new InvalidDataException("The voice-model download was larger than expected.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report((int)(downloaded * 100 / model.Size));
                }
            }
            await VerifyAsync(model, temporary, token);
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
