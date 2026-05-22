using System.IO;
using System.Net.Sockets;
using TcpMessenger.Shared;

namespace TcpMessenger.Client.Services;

public sealed class TcpClientService
{
    private const int ChunkSize = 64 * 1024;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public event Action<NetworkMessage>? MessageReceived;
    public event Action<string>? ConnectionChanged;
    public event Action<string, int>? UploadProgressChanged;
    public event Action<string, int>? DownloadProgressChanged;
    public event Action<string>? FileSaved;

    public bool IsConnected => _client?.Connected == true;
    public string UserName { get; private set; } = string.Empty;

    public async Task ConnectAsync(string host, int port, string userName)
    {
        _cts = new CancellationTokenSource();
        _client = new TcpClient();
        await _client.ConnectAsync(host, port, _cts.Token);
        _stream = _client.GetStream();
        UserName = userName;

        await SendAsync(new NetworkMessage
        {
            Type = MessageType.Login,
            From = userName
        });

        ConnectionChanged?.Invoke("Connected");
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public async Task DisconnectAsync()
    {
        if (_stream is not null)
        {
            try
            {
                await SendAsync(new NetworkMessage
                {
                    Type = MessageType.Disconnect,
                    From = UserName
                });
            }
            catch { }
        }

        Close();
        ConnectionChanged?.Invoke("Disconnected");
    }

    public Task SendChatAsync(string text) => SendAsync(new NetworkMessage
    {
        Type = MessageType.Chat,
        From = UserName,
        Text = text
    });

    public Task SendPrivateChatAsync(string to, string text) => SendAsync(new NetworkMessage
    {
        Type = MessageType.PrivateChat,
        From = UserName,
        To = to,
        Text = text
    });

    public async Task UploadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found.", filePath);

        var info = new FileInfo(filePath);
        string fileId = Guid.NewGuid().ToString("N");
        int totalChunks = (int)Math.Ceiling(info.Length / (double)ChunkSize);

        await SendAsync(new NetworkMessage
        {
            Type = MessageType.FileUploadStart,
            From = UserName,
            FileId = fileId,
            FileName = info.Name,
            FileSize = info.Length,
            TotalChunks = totalChunks
        });

        byte[] buffer = new byte[ChunkSize];
        await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
        int index = 0;
        int read;
        while ((read = await file.ReadAsync(buffer)) > 0)
        {
            byte[] chunk = buffer[..read];
            await SendAsync(new NetworkMessage
            {
                Type = MessageType.FileUploadChunk,
                From = UserName,
                FileId = fileId,
                FileName = info.Name,
                FileSize = info.Length,
                ChunkIndex = index,
                TotalChunks = totalChunks,
                DataBase64 = Convert.ToBase64String(chunk)
            });

            index++;
            int progress = totalChunks == 0 ? 100 : (int)(index * 100.0 / totalChunks);
            UploadProgressChanged?.Invoke(fileId, progress);
        }
    }

    public Task RequestFileAsync(string fileId) => SendAsync(new NetworkMessage
    {
        Type = MessageType.FileDownloadRequest,
        From = UserName,
        FileId = fileId
    });

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                NetworkMessage? message = await MessageFramer.ReadAsync(_stream, cancellationToken);
                if (message is null)
                    break;

                if (message.Type == MessageType.FileDownloadStart)
                {
                    await ReceiveFileAsync(message, cancellationToken);
                    continue;
                }

                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex)
        {
            MessageReceived?.Invoke(new NetworkMessage
            {
                Type = MessageType.Error,
                From = "Client",
                Text = ex.Message,
                Success = false
            });
        }
        finally
        {
            Close();
            ConnectionChanged?.Invoke("Disconnected");
        }
    }

    private async Task ReceiveFileAsync(NetworkMessage startMessage, CancellationToken cancellationToken)
    {
        string fileId = startMessage.FileId ?? Guid.NewGuid().ToString("N");
        string fileName = Path.GetFileName(startMessage.FileName ?? "download.bin");
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TcpMessengerDownloads");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);

        if (File.Exists(path))
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            path = Path.Combine(folder, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
        }

        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, ChunkSize, true);
        int received = 0;
        int total = Math.Max(startMessage.TotalChunks, 1);

        while (received < total && _stream is not null)
        {
            NetworkMessage? chunkMessage = await MessageFramer.ReadAsync(_stream, cancellationToken);
            if (chunkMessage is null)
                break;

            if (chunkMessage.Type != MessageType.FileDownloadChunk || chunkMessage.FileId != fileId)
            {
                MessageReceived?.Invoke(chunkMessage);
                continue;
            }

            byte[] data = Convert.FromBase64String(chunkMessage.DataBase64 ?? string.Empty);
            await file.WriteAsync(data, cancellationToken);
            received++;
            DownloadProgressChanged?.Invoke(fileId, (int)(received * 100.0 / total));
        }

        FileSaved?.Invoke(path);
    }

    private async Task SendAsync(NetworkMessage message)
    {
        if (_stream is null)
            throw new InvalidOperationException("Client is not connected.");

        await _sendLock.WaitAsync();
        try
        {
            await MessageFramer.WriteAsync(_stream, message, _cts?.Token ?? CancellationToken.None);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private void Close()
    {
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Close(); } catch { }
        try { _client?.Close(); } catch { }
        _stream = null;
        _client = null;
    }
}
