using TcpMessenger.Server.Services;

Console.Title = "TCP Messenger Server";
Console.WriteLine("TCP Messenger Server");
Console.WriteLine("Press Ctrl+C to stop.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var server = new ChatServer(port: 5000);
await server.StartAsync(cts.Token);
