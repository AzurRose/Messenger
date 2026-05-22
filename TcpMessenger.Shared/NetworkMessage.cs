using System.Text.Json.Serialization;

namespace TcpMessenger.Shared;

public sealed class NetworkMessage
{
    public MessageType Type { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Text { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;

    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public int ChunkIndex { get; set; }
    public int TotalChunks { get; set; }
    public string? DataBase64 { get; set; }

    public List<string>? Users { get; set; }
    public bool Success { get; set; } = true;
}
