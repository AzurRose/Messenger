using System.Buffers.Binary;
using System.Text.Json;

namespace TcpMessenger.Shared;

public static class MessageFramer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task WriteAsync(Stream stream, NetworkMessage message, CancellationToken cancellationToken = default)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);

        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<NetworkMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] lengthBuffer = new byte[4];
        bool hasLength = await ReadExactAsync(stream, lengthBuffer, cancellationToken);
        if (!hasLength)
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (length <= 0 || length > 25 * 1024 * 1024)
            throw new InvalidDataException("Invalid message size.");

        byte[] payload = new byte[length];
        bool hasPayload = await ReadExactAsync(stream, payload, cancellationToken);
        if (!hasPayload)
            return null;

        return JsonSerializer.Deserialize<NetworkMessage>(payload, JsonOptions);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0)
                return false;

            offset += read;
        }

        return true;
    }
}
