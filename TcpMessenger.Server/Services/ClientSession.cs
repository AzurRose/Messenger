using System.Net.Sockets;
using TcpMessenger.Shared;

namespace TcpMessenger.Server.Services;

public sealed class ClientSession
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public ClientSession(TcpClient client)
    {
        Client = client;
        Stream = client.GetStream();
    }

    public TcpClient Client { get; }
    public NetworkStream Stream { get; }
    public string UserName { get; set; } = string.Empty;

    public async Task SendAsync(NetworkMessage message, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await MessageFramer.WriteAsync(Stream, message, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Close()
    {
        try { Client.Close(); } catch { }
    }
}
