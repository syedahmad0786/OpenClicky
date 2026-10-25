using System.Buffers.Binary;
using System.Text.Json;

namespace OpenClicky.Providers;

/// <summary>Chrome native-messaging framing. Authentication belongs in the first protocol message.</summary>
public static class ChromeBridgeNativeProtocol
{
    public const int MaxMessageBytes = 1_048_576;
    /// <summary>Chrome → host/app direction (screenshots); host → Chrome stays at <see cref="MaxMessageBytes"/>.</summary>
    public const int MaxInboundBytes = 8 * 1024 * 1024;

    public static async Task<JsonDocument?> ReadAsync(Stream input, CancellationToken token, int maxBytes = MaxMessageBytes)
    {
        var header = new byte[4];
        var read = await ReadExactlyOrEndAsync(input, header, token).ConfigureAwait(false);
        if (!read) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > maxBytes) throw new InvalidDataException("Invalid Chrome native-message length.");
        var payload = new byte[length];
        await input.ReadExactlyAsync(payload, token).ConfigureAwait(false);
        return JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 32 });
    }

    public static async Task WriteAsync(Stream output, object message, CancellationToken token, int maxBytes = MaxMessageBytes)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payload.Length > maxBytes) throw new InvalidDataException("Chrome native message exceeds the size limit.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await output.WriteAsync(header, token).ConfigureAwait(false);
        await output.WriteAsync(payload, token).ConfigureAwait(false);
        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream input, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(offset), token).ConfigureAwait(false);
            if (read == 0) return offset == 0 ? false : throw new EndOfStreamException("Truncated Chrome native-message header.");
            offset += read;
        }
        return true;
    }
}
