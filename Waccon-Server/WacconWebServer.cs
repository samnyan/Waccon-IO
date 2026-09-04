using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace Waccon.Server;

/// <summary>Serves the bundled controller and translates its WebSocket messages into WCON frames.</summary>
public sealed class WacconWebServer : IDisposable
{
    private const int MaxHttpHeaderBytes = 8192;
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private readonly ServerOptions _options;
    private readonly SharedMemoryBridge _bridge;
    private readonly TcpListener _listener;
    private readonly Func<bool> _tryReserveClient;
    private readonly Action _releaseClient;
    private readonly Action<byte[]> _publishInput;
    private readonly Action _clearInput;

    /// <summary>Creates the localhost HTTP and WebSocket endpoint.</summary>
    public WacconWebServer(ServerOptions options, SharedMemoryBridge bridge, Func<bool> tryReserveClient, Action releaseClient, Action<byte[]> publishInput, Action clearInput)
    {
        _options = options;
        _bridge = bridge;
        _tryReserveClient = tryReserveClient;
        _releaseClient = releaseClient;
        _publishInput = publishInput;
        _clearInput = clearInput;
        _listener = new TcpListener(IPAddress.Parse(options.ListenAddress), options.WebPort);
    }

    /// <summary>Starts accepting controller page and WebSocket requests.</summary>
    public async Task RunAsync(CancellationToken token)
    {
        _listener.Start();
        try {
            while (!token.IsCancellationRequested) {
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = HandleConnectionAsync(client, token);
            }
        } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally {
            _listener.Stop();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Stop();

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client) {
            client.NoDelay = true;
            using var stream = client.GetStream();
            try {
                var request = await ReadRequestAsync(stream, serverToken).ConfigureAwait(false);
                if (request is null) return;
                if (request.Value.Path == "/ws") {
                    await HandleWebSocketAsync(stream, request.Value, serverToken).ConfigureAwait(false);
                    return;
                }
                await HandleStaticRequestAsync(stream, request.Value, serverToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (serverToken.IsCancellationRequested) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    private async Task HandleStaticRequestAsync(NetworkStream stream, HttpRequest request, CancellationToken token)
    {
        if (request.Method != "GET") {
            await WriteHttpResponseAsync(stream, "405 Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed"u8.ToArray(), token).ConfigureAwait(false);
            return;
        }
        if (request.Path == "/health") {
            await WriteHttpResponseAsync(stream, "200 OK", "application/json; charset=utf-8", _bridge.GetHealthPayload(), token).ConfigureAwait(false);
            return;
        }
        var file = request.Path switch {
            "/" or "/index.html" or "/web" or "/web/" or "/web/index.html" => "index.html",
            _ when request.Path.StartsWith("/web/", StringComparison.Ordinal) => request.Path[5..],
            _ => null,
        };
        if (file is null || !TryGetWebAssetPath(file, out var assetPath)) {
            await WriteHttpResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found"u8.ToArray(), token).ConfigureAwait(false);
            return;
        }
        var contentType = file.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript; charset=utf-8"
            : file.EndsWith(".css", StringComparison.Ordinal) ? "text/css; charset=utf-8"
            : "text/html; charset=utf-8";
        var content = await File.ReadAllBytesAsync(assetPath, token).ConfigureAwait(false);
        await WriteHttpResponseAsync(stream, "200 OK", contentType, content, token).ConfigureAwait(false);
    }

    private static bool TryGetWebAssetPath(string file, out string assetPath)
    {
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "web"));
        assetPath = Path.GetFullPath(Path.Combine(webRoot, file));
        if (assetPath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(assetPath)) return true;

        // Development/published layouts may place the generated bundle beside
        // the executable or retain the source asset names.
        var fallbackRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "web-dist"));
        assetPath = Path.GetFullPath(Path.Combine(fallbackRoot, file));
        return assetPath.StartsWith(fallbackRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(assetPath);
    }

    private async Task HandleWebSocketAsync(NetworkStream stream, HttpRequest request, CancellationToken serverToken)
    {
        if (request.Method != "GET" || request.WebSocketKey is null) {
            await WriteHttpResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "WebSocket upgrade required"u8.ToArray(), serverToken).ConfigureAwait(false);
            return;
        }
        if (!_tryReserveClient()) {
            await WriteHttpResponseAsync(stream, "503 Service Unavailable", "text/plain; charset=utf-8", "Controller capacity reached"u8.ToArray(), serverToken).ConfigureAwait(false);
            return;
        }

        try {
            await WriteWebSocketHandshakeAsync(stream, request.WebSocketKey, serverToken).ConfigureAwait(false);
            using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
            using var clientStop = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
            using var sendLock = new SemaphoreSlim(1, 1);
            var hello = await ReceiveFrameAsync(socket, clientStop.Token).ConfigureAwait(false);
            if (hello is null || hello.Value.Type != MessageType.Hello || !Authorized(hello.Value.Payload)) {
                await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "HELLO required", clientStop.Token).ConfigureAwait(false);
                return;
            }
            await SendFrameAsync(socket, sendLock, WconProtocol.Frame(MessageType.Welcome, hello.Value.Sequence, _bridge.GetWelcomePayload()), clientStop.Token).ConfigureAwait(false);
            var ledTask = SendLedUpdatesAsync(socket, sendLock, clientStop.Token);
            try {
                while (!clientStop.IsCancellationRequested) {
                    var packet = await ReceiveFrameAsync(socket, clientStop.Token).ConfigureAwait(false);
                    if (packet is null) break;
                    switch (packet.Value.Type) {
                        case MessageType.InputSnapshot:
                            _publishInput(packet.Value.Payload);
                            break;
                        case MessageType.ClearInput:
                            _clearInput();
                            break;
                        case MessageType.Ping:
                            await SendFrameAsync(socket, sendLock, WconProtocol.Frame(MessageType.Pong, packet.Value.Sequence, packet.Value.Payload), clientStop.Token).ConfigureAwait(false);
                            break;
                    }
                }
            } finally {
                clientStop.Cancel();
                try { await ledTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            }
        } catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (InvalidDataException) { }
        finally {
            _clearInput();
            _releaseClient();
        }
    }

    private async Task SendLedUpdatesAsync(WebSocket socket, SemaphoreSlim sendLock, CancellationToken token)
    {
        uint sequence = 0;
        byte[]? previous = null;
        while (!token.IsCancellationRequested) {
            var current = _bridge.ReadLedSnapshot();
            if (current is not null && !current.AsSpan().SequenceEqual(previous)) {
                await SendFrameAsync(socket, sendLock, WconProtocol.Frame(MessageType.LedSnapshot, sequence++, current), token).ConfigureAwait(false);
                previous = current;
            }
            await Task.Delay(33, token).ConfigureAwait(false);
        }
    }

    private async Task<(MessageType Type, uint Sequence, byte[] Payload)?> ReceiveFrameAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[WconProtocol.HeaderSize + _options.MaxPayloadBytes];
        var used = 0;
        while (true) {
            var result = await socket.ReceiveAsync(buffer.AsMemory(used), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("WCON WebSocket messages must be binary.");
            used += result.Count;
            if (used == buffer.Length && !result.EndOfMessage) throw new InvalidDataException("WCON WebSocket message exceeds configured limit.");
            if (result.EndOfMessage) return WconProtocol.ParseFrame(buffer.AsSpan(0, used), _options.MaxPayloadBytes);
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken token)
    {
        var bytes = new List<byte>(1024);
        while (bytes.Count < MaxHttpHeaderBytes) {
            var next = new byte[1];
            if (await stream.ReadAsync(next, token).ConfigureAwait(false) == 0) return null;
            bytes.Add(next[0]);
            var count = bytes.Count;
            if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n') break;
        }
        if (bytes.Count == MaxHttpHeaderBytes) throw new InvalidDataException("HTTP request header exceeds configured limit.");
        var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n", StringSplitOptions.None);
        var firstLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (firstLine.Length != 3) throw new InvalidDataException("Invalid HTTP request line.");
        string? webSocketKey = null;
        foreach (var line in lines.Skip(1)) {
            var separator = line.IndexOf(':');
            if (separator <= 0) continue;
            if (line[..separator].Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase)) webSocketKey = line[(separator + 1)..].Trim();
        }
        var path = firstLine[1].Split('?', 2)[0];
        return new HttpRequest(firstLine[0], path, webSocketKey);
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, string status, string contentType, byte[] content, CancellationToken token)
    {
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {content.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(content, token).ConfigureAwait(false);
    }

    private static async Task WriteWebSocketHandshakeAsync(NetworkStream stream, string key, CancellationToken token)
    {
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n");
        await stream.WriteAsync(response, token).ConfigureAwait(false);
    }

    private static async Task SendFrameAsync(WebSocket socket, SemaphoreSlim sendLock, byte[] frame, CancellationToken token)
    {
        await sendLock.WaitAsync(token).ConfigureAwait(false);
        try {
            await socket.SendAsync(frame.AsMemory(), WebSocketMessageType.Binary, endOfMessage: true, token).ConfigureAwait(false);
        } finally {
            sendLock.Release();
        }
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description, CancellationToken token)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) {
            await socket.CloseAsync(status, description, token).ConfigureAwait(false);
        }
    }

    private bool Authorized(ReadOnlySpan<byte> payload)
    {
        return _options.AuthToken.Length == 0 || payload.SequenceEqual(Encoding.UTF8.GetBytes(_options.AuthToken));
    }

    private readonly record struct HttpRequest(string Method, string Path, string? WebSocketKey);
}
