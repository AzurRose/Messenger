namespace TcpMessenger.Client.Models;

public sealed class FileTransferItem : TcpMessenger.Client.ViewModels.ViewModelBase
{
    private int _progress;
    private string _status = "Available";

    public string FileId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string UploadedBy { get; set; } = string.Empty;

    public int Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string Display => $"{FileName} ({FileSize / 1024.0:0.0} KB)";
}
