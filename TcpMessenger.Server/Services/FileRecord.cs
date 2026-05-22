namespace TcpMessenger.Server.Services;

public sealed class FileRecord
{
    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.Now;
    public string Path { get; set; } = string.Empty;
}
