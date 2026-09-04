using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Waccon.Server;

var options = ServerOptions.Load(args);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Waccon Server tcp listening on {options.ListenAddress}:{options.TcpPort}");
Console.WriteLine($"Waccon Server websocket listening on {options.ListenAddress}:{options.WebPort}");
Console.WriteLine("Web controller:");
var addresses = NetworkInterface.GetAllNetworkInterfaces()
    .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
    .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses
        .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(unicast => unicast.Address))
    .Append(IPAddress.Loopback)
    .Distinct()
    .OrderBy(address => address.Equals(IPAddress.Loopback))
    .ThenBy(address => address.ToString(), StringComparer.Ordinal)
    .ToArray();

foreach (var address in addresses)
{
    Console.WriteLine($"    http://{address}:{options.WebPort}/web/");
}

Console.WriteLine($"Shared memory: {options.SharedMemoryName}");
await using var server = new WacconServer(options);
await server.RunAsync(cts.Token).ConfigureAwait(false);
