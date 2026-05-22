namespace TcpMessenger.Shared;

public enum MessageType
{
    Login,
    Chat,
    PrivateChat,
    UserList,
    System,
    FileUploadStart,
    FileUploadChunk,
    FileAvailable,
    FileDownloadRequest,
    FileDownloadStart,
    FileDownloadChunk,
    Error,
    Disconnect
}
