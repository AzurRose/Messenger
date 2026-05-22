using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using TcpMessenger.Shared;

namespace TcpMessenger.Server.Services;

public sealed class ChatServer
{
    private const int ChunkSize = 64 * 1024;
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileRecord> _files = new();
    private readonly ConcurrentDictionary<string, UploadState> _uploads = new();
    private readonly string _storagePath;

    public ChatServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
        _storagePath = Path.Combine(AppContext.BaseDirectory, "ServerStorage");
        Directory.CreateDirectory(_storagePath);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        Console.WriteLine($"TCP server started on port {((IPEndPoint)_listener.LocalEndpoint).Port}");
        Console.WriteLine($"File storage: {_storagePath}");

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        var session = new ClientSession(tcpClient);
        try
        {
            NetworkMessage? login = await MessageFramer.ReadAsync(session.Stream, cancellationToken);
            if (login is null || login.Type != MessageType.Login || string.IsNullOrWhiteSpace(login.From))
            {
                await session.SendAsync(Error("Login message is required."), cancellationToken);
                return;
            }

            string userName = login.From.Trim();
            if (!_sessions.TryAdd(userName, session))
            {
                await session.SendAsync(Error("User with this name is already connected."), cancellationToken);
                return;
            }

            session.UserName = userName;
            Console.WriteLine($"Connected: {userName}");

            await session.SendAsync(new NetworkMessage
            {
                Type = MessageType.System,
                Text = "Connected to server.",
                From = "Server"
            }, cancellationToken);

            await BroadcastSystemAsync($"{userName} connected.", cancellationToken);
            await BroadcastUserListAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                NetworkMessage? message = await MessageFramer.ReadAsync(session.Stream, cancellationToken);
                if (message is null)
                    break;

                message.From = session.UserName;
                await ProcessMessageAsync(session, message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client error: {ex.Message}");
        }
        finally
        {
            await DisconnectAsync(session, cancellationToken);
        }
    }

    private async Task ProcessMessageAsync(ClientSession sender, NetworkMessage message, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case MessageType.Chat:
                await BroadcastAsync(new NetworkMessage
                {
                    Type = MessageType.Chat,
                    From = sender.UserName,
                    Text = message.Text,
                    Date = DateTime.Now
                }, cancellationToken);
                break;

            case MessageType.PrivateChat:
                await SendPrivateAsync(sender, message, cancellationToken);
                break;

            case MessageType.FileUploadStart:
                await RegisterUploadAsync(sender, message, cancellationToken);
                break;

            case MessageType.FileUploadChunk:
                await ReceiveFileChunkAsync(sender, message, cancellationToken);
                break;

            case MessageType.FileDownloadRequest:
                await SendFileAsync(sender, message.FileId, cancellationToken);
                break;

            case MessageType.Disconnect:
                sender.Close();
                break;
        }
    }

    private async Task SendPrivateAsync(ClientSession sender, NetworkMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.To) || !_sessions.TryGetValue(message.To, out ClientSession? receiver))
        {
            await sender.SendAsync(Error("Receiver is not connected."), cancellationToken);
            return;
        }

        var outgoing = new NetworkMessage
        {
            Type = MessageType.PrivateChat,
            From = sender.UserName,
            To = receiver.UserName,
            Text = message.Text,
            Date = DateTime.Now
        };

        await receiver.SendAsync(outgoing, cancellationToken);
        await sender.SendAsync(outgoing, cancellationToken);
    }

    private async Task RegisterUploadAsync(ClientSession sender, NetworkMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.FileId) || string.IsNullOrWhiteSpace(message.FileName))
        {
            await sender.SendAsync(Error("Invalid file metadata."), cancellationToken);
            return;
        }

        string safeName = Path.GetFileName(message.FileName);
        string path = Path.Combine(_storagePath, $"{message.FileId}_{safeName}");
        if (File.Exists(path))
            File.Delete(path);

        _uploads[message.FileId] = new UploadState
        {
            FileId = message.FileId,
            FileName = safeName,
            FileSize = message.FileSize,
            UploadedBy = sender.UserName,
            Path = path,
            TotalChunks = message.TotalChunks
        };

        await sender.SendAsync(new NetworkMessage
        {
            Type = MessageType.System,
            From = "Server",
            Text = $"Upload started: {safeName}"
        }, cancellationToken);
    }

    private async Task ReceiveFileChunkAsync(ClientSession sender, NetworkMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.FileId) || !_uploads.TryGetValue(message.FileId, out UploadState? upload))
        {
            await sender.SendAsync(Error("Upload session not found."), cancellationToken);
            return;
        }

        byte[] data = Convert.FromBase64String(message.DataBase64 ?? string.Empty);
        await using (var file = new FileStream(upload.Path, FileMode.Append, FileAccess.Write, FileShare.Read, 64 * 1024, true))
        {
            await file.WriteAsync(data, cancellationToken);
        }

        upload.ReceivedChunks++;

        if (upload.ReceivedChunks >= upload.TotalChunks)
        {
            _uploads.TryRemove(upload.FileId, out _);
            var record = new FileRecord
            {
                FileId = upload.FileId,
                FileName = upload.FileName,
                FileSize = upload.FileSize,
                UploadedBy = upload.UploadedBy,
                UploadedAt = DateTime.Now,
                Path = upload.Path
            };
            _files[record.FileId] = record;

            await BroadcastAsync(new NetworkMessage
            {
                Type = MessageType.FileAvailable,
                From = "Server",
                FileId = record.FileId,
                FileName = record.FileName,
                FileSize = record.FileSize,
                Text = $"{record.UploadedBy} uploaded file: {record.FileName}",
                Date = DateTime.Now
            }, cancellationToken);
        }
    }

    private async Task SendFileAsync(ClientSession receiver, string? fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId) || !_files.TryGetValue(fileId, out FileRecord? record) || !File.Exists(record.Path))
        {
            await receiver.SendAsync(Error("File not found."), cancellationToken);
            return;
        }

        int totalChunks = (int)Math.Ceiling(record.FileSize / (double)ChunkSize);
        await receiver.SendAsync(new NetworkMessage
        {
            Type = MessageType.FileDownloadStart,
            From = "Server",
            FileId = record.FileId,
            FileName = record.FileName,
            FileSize = record.FileSize,
            TotalChunks = totalChunks
        }, cancellationToken);

        byte[] buffer = new byte[ChunkSize];
        await using var file = new FileStream(record.Path, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize, true);
        int index = 0;
        int read;
        while ((read = await file.ReadAsync(buffer, cancellationToken)) > 0)
        {
            byte[] chunk = buffer[..read];
            await receiver.SendAsync(new NetworkMessage
            {
                Type = MessageType.FileDownloadChunk,
                From = "Server",
                FileId = record.FileId,
                FileName = record.FileName,
                FileSize = record.FileSize,
                ChunkIndex = index,
                TotalChunks = totalChunks,
                DataBase64 = Convert.ToBase64String(chunk)
            }, cancellationToken);
            index++;
        }
    }

    private async Task DisconnectAsync(ClientSession session, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(session.UserName) && _sessions.TryRemove(session.UserName, out _))
        {
            Console.WriteLine($"Disconnected: {session.UserName}");
            await BroadcastSystemAsync($"{session.UserName} disconnected.", cancellationToken);
            await BroadcastUserListAsync(cancellationToken);
        }

        session.Close();
    }

    private async Task BroadcastUserListAsync(CancellationToken cancellationToken)
    {
        await BroadcastAsync(new NetworkMessage
        {
            Type = MessageType.UserList,
            From = "Server",
            Users = _sessions.Keys.OrderBy(x => x).ToList()
        }, cancellationToken);
    }

    private async Task BroadcastSystemAsync(string text, CancellationToken cancellationToken)
    {
        await BroadcastAsync(new NetworkMessage
        {
            Type = MessageType.System,
            From = "Server",
            Text = text,
            Date = DateTime.Now
        }, cancellationToken);
    }

    private async Task BroadcastAsync(NetworkMessage message, CancellationToken cancellationToken)
    {
        foreach (ClientSession session in _sessions.Values.ToList())
        {
            try
            {
                await session.SendAsync(message, cancellationToken);
            }
            catch
            {
                session.Close();
            }
        }
    }

    private static NetworkMessage Error(string text) => new()
    {
        Type = MessageType.Error,
        From = "Server",
        Text = text,
        Success = false
    };
}
