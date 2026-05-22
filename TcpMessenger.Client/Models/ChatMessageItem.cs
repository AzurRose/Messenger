namespace TcpMessenger.Client.Models;

public sealed class ChatMessageItem
{
    public DateTime Date { get; set; } = DateTime.Now;
    public string Sender { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;

    public string Display => string.IsNullOrWhiteSpace(Target)
        ? $"[{Date:HH:mm:ss}] {Sender}: {Text}"
        : $"[{Date:HH:mm:ss}] {Sender} -> {Target}: {Text}";
}
