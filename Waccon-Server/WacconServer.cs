using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;

namespace Waccon.Server;

/// <summary>Console TCP bridge between WCON clients and waccon-io shared memory.</summary>
public sealed class WacconServer : IAsyncDisposable
{
    private readonly ServerOptions _options;
    private readonly SharedMemoryBridge _bridge;
    private readonly TcpListener _listener;
    private readonly WacconWebServer _webServer;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _inputDebugLock = new();
    private byte[]? _lastDebugInput;
    private int _clientCount;

    /// <summary>Creates a server with validated options.</summary>
    public WacconServer(ServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _bridge = new SharedMemoryBridge(options.SharedMemoryName);
        _listener = new TcpListener(IPAddress.Parse(options.ListenAddress), options.TcpPort);
        _webServer = new WacconWebServer(options, _bridge, TryReserveClient, ReleaseClient, PublishInput, ClearInput);
    }

    /// <summary>Accepts clients until cancellation.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        _listener.Start();
        var webTask = _webServer.RunAsync(linked.Token);
        if (webTask.IsFaulted) await webTask.ConfigureAwait(false);
        var diagnosticsTask = MonitorSharedMemoryAsync(linked.Token);
        try {
            while (!linked.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(linked.Token).ConfigureAwait(false);
                if (!TryReserveClient()) {
                    client.Dispose();
                    continue;
                }
                _ = HandleClientAsync(client, linked.Token);
            }
        } catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        finally {
            linked.Cancel();
            _listener.Stop();
            try { await diagnosticsTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        _webServer.Dispose();
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
                await stream.WriteAsync(WconProtocol.Frame(MessageType.Welcome, 0, _bridge.GetWelcomePayload()), serverToken).ConfigureAwait(false);
                using var clientStop = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                var ledTask = SendLedUpdatesAsync(stream, clientStop.Token);
                while (!clientStop.IsCancellationRequested) {
                    var packet = await WconProtocol.ReadAsync(stream, _options.MaxPayloadBytes, clientStop.Token).ConfigureAwait(false);
                    if (packet is null) break;
                    switch (packet.Value.Type) {
                        case MessageType.InputSnapshot:
                            PublishInput(packet.Value.Payload);
                            break;
                        case MessageType.ClearInput:
                            ClearInput();
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
                ClearInput();
                ReleaseClient();
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

    private bool TryReserveClient()
    {
        if (Interlocked.Increment(ref _clientCount) <= _options.MaxClients) return true;
        Interlocked.Decrement(ref _clientCount);
        return false;
    }

    private void ReleaseClient() => Interlocked.Decrement(ref _clientCount);

    private void PublishInput(byte[] payload)
    {
        _bridge.PublishInput(payload, _options.LeaseTimeoutMs);
        if (!_options.DebugInput) return;

        var snapshot = payload.AsSpan(0, 242).ToArray();
        lock (_inputDebugLock) {
            if (_lastDebugInput is not null && snapshot.AsSpan().SequenceEqual(_lastDebugInput)) return;
            _lastDebugInput = snapshot;
            var sourceId = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(WconProtocol.SourceIdOffset));
            Console.WriteLine($"[input] source={sourceId} activeCells={FormatActiveCells(snapshot)}");
        }
    }

    private void ClearInput()
    {
        _bridge.ClearInput();
        if (!_options.DebugInput) return;

        lock (_inputDebugLock) {
            if (_lastDebugInput is not null && _lastDebugInput.AsSpan(2).IndexOfAnyExcept((byte)0) < 0) return;
            _lastDebugInput = new byte[242];
            Console.WriteLine("[input] activeCells=<none>");
        }
    }

    private static string FormatActiveCells(ReadOnlySpan<byte> snapshot)
    {
        var cells = new List<int>();
        for (var cell = 0; cell < WconProtocol.TouchCellCount; cell++) {
            if (snapshot[WconProtocol.TouchCellOffset + cell] != 0) cells.Add(cell);
        }
        return cells.Count == 0 ? "<none>" : string.Join(',', cells);
    }

    private async Task MonitorSharedMemoryAsync(CancellationToken token)
    {
        bool? wasConnected = null;
        while (!token.IsCancellationRequested) {
            _bridge.Heartbeat();
            var status = _bridge.GetStatus();
            if (wasConnected != status.IoConnected) {
                Console.WriteLine(status.IoConnected
                    ? $"waccon-io connected (pid {status.IoProcessId}, input sequence {status.IoInputSequence})."
                    : "waccon-io not detected; waiting for a compatible DLL to enter mercury_io_poll().");
                wasConnected = status.IoConnected;
            }
            await Task.Delay(1000, token).ConfigureAwait(false);
        }
    }
}
