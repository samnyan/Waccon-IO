using System.Net;
using System.Net.Sockets;

namespace Waccon.Server;

/// <summary>Console TCP bridge between WCON clients and waccon-io shared memory.</summary>
public sealed class WacconServer : IAsyncDisposable
{
    private readonly ServerOptions _options;
    private readonly SharedMemoryBridge _bridge;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private int _clientCount;

    /// <summary>Creates a server with validated options.</summary>
    public WacconServer(ServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _bridge = new SharedMemoryBridge(options.SharedMemoryName);
        _listener = new TcpListener(IPAddress.Parse(options.ListenAddress), options.TcpPort);
    }

    /// <summary>Accepts clients until cancellation.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        _listener.Start();
        try {
            while (!linked.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
                if (Interlocked.Increment(ref _clientCount) > _options.MaxClients) {
                    Interlocked.Decrement(ref _clientCount);
                    client.Dispose();
                    continue;
                }
                _ = HandleClientAsync(client, linked.Token);
            }
        } catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally { _listener.Stop(); }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        _bridge.Dispose();
        await ValueTask.CompletedTask;
        _stop.Dispose();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client) {
            client.NoDelay = true;
            using var stream = client.GetStream();
            try {
                var hello = await WconProtocol.ReadAsync(stream, _options.MaxPayloadBytes, serverToken).ConfigureAwait(false);
                if (hello is null || hello.Value.Type != MessageType.Hello || !Authorized(hello.Value.Payload)) return;
                await stream.WriteAsync(WconProtocol.Frame(MessageType.Welcome, 0, "Waccon"u8), serverToken).ConfigureAwait(false);
                using var clientStop = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                var ledTask = SendLedUpdatesAsync(stream, clientStop.Token);
                while (!clientStop.IsCancellationRequested) {
                    var packet = await WconProtocol.ReadAsync(stream, _options.MaxPayloadBytes, clientStop.Token).ConfigureAwait(false);
                    if (packet is null) break;
                    switch (packet.Value.Type) {
                        case MessageType.InputSnapshot:
                            _bridge.PublishInput(packet.Value.Payload, _options.LeaseTimeoutMs);
                            break;
                        case MessageType.ClearInput:
                            _bridge.ClearInput();
                            break;
                        case MessageType.Ping:
                            await stream.WriteAsync(WconProtocol.Frame(MessageType.Pong, packet.Value.Sequence, packet.Value.Payload), clientStop.Token).ConfigureAwait(false);
                            break;
                    }
                }
                clientStop.Cancel();
                await ledTask.ConfigureAwait(false);
            } catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
            finally {
                _bridge.ClearInput();
                Interlocked.Decrement(ref _clientCount);
            }
        }
    }

    private async Task SendLedUpdatesAsync(NetworkStream stream, CancellationToken token)
    {
        uint sequence = 0;
        byte[]? previous = null;
        while (!token.IsCancellationRequested) {
            var current = _bridge.ReadLedSnapshot();
            if (current is not null && !current.AsSpan().SequenceEqual(previous)) {
                await stream.WriteAsync(WconProtocol.Frame(MessageType.LedSnapshot, sequence++, current), token).ConfigureAwait(false);
                previous = current;
            }
            await Task.Delay(33, token).ConfigureAwait(false);
        }
    }

    private bool Authorized(ReadOnlySpan<byte> payload)
    {
        if (_options.AuthToken.Length == 0) return true;
        return payload.SequenceEqual(System.Text.Encoding.UTF8.GetBytes(_options.AuthToken));
    }
}
