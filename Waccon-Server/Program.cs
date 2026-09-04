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

Console.WriteLine($"Waccon Server listening on {options.ListenAddress}:{options.TcpPort}");
Console.WriteLine("Network addresses:");
var addresses = NetworkInterface.GetAllNetworkInterfaces()
    .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
    .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses
        .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(unicast => unicast.Address)))
    .Distinct()
    .OrderBy(address => address.Equals(IPAddress.Loopback))
    .ThenBy(address => address.ToString(), StringComparer.Ordinal)
    .ToArray();

if (addresses.Length == 0)
{
    Console.WriteLine("  (no IPv4 network addresses found)");
}
else
{
    foreach (var address in addresses)
    {
        Console.WriteLine($"  {address}");
        Console.WriteLine($"    Web: http://{address}:{options.WebPort}/web/");
    }
}

Console.WriteLine($"Web controller: http://{options.ListenAddress}:{options.WebPort}/web/");
Console.WriteLine($"Shared memory: {options.SharedMemoryName}");
await using var server = new WacconServer(options);
await server.RunAsync(cts.Token).ConfigureAwait(false);
