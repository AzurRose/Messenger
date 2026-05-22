namespace TcpMessenger.Server.Services;

public sealed class UploadState
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public int ReceivedChunks { get; set; }
}
