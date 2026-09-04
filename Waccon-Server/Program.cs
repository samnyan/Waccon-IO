using System.Text.Json;
using Waccon.Server;

var options = ServerOptions.Load(args);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Waccon Server listening on {options.ListenAddress}:{options.TcpPort}");
Console.WriteLine($"Web controller: http://{options.ListenAddress}:{options.WebPort}/web/");
Console.WriteLine($"Shared memory: {options.SharedMemoryName}");
await using var server = new WacconServer(options);
await server.RunAsync(cts.Token).ConfigureAwait(false);
