# Waccon Server

C# console bridge between WCON clients and the native `waccon_io.dll` shared-memory ABI.

It also serves the bundled `toucca/web`-derived virtual controller and a real
WebSocket endpoint. The page is the first WCON validation client; it keeps the
original 240-cell circular touch layout while using binary WCON frames instead
of toucca's old 30-byte raw WebSocket payload.

## Build

Install the web build dependency once after cloning:

```bat
npm ci
```

```bat
build.bat build
build.bat test
build.bat publish
```

Native AOT output is generated under:

```text
bin\Release\net10.0-windows\win-x64\publish\Waccon.Server.exe
```

The executable is self-contained and does not require a .NET runtime on the target machine.

### Web controller bundle

The controller source lives in `web/`. Vite compiles it into the ignored
`web-dist/` build directory:

```bat
npm run web:build
```

`dotnet build` and `dotnet publish` run this command automatically. The publish
target then copies only these generated files into `publish\web\`:

```text
web\index.html
web\controller.js
web\style.css
```

`controller.js` contains the touch mapping and WCON encoder in one browser
bundle, so the page does not make a separate `controller-core.js` request.
Node.js is required only to build the Server from source; it is not required on
the machine running the published executable.

## Configuration

The server reads `appsettings.json` from its working directory. It defaults to localhost only:

```json
{
  "server": {
    "listenAddress": "127.0.0.1",
    "tcpPort": 52468,
    "webPort": 52469,
    "maxClients": 4,
    "leaseTimeoutMs": 500,
    "maxPayloadBytes": 4096,
    "authToken": "",
    "debugInput": false
  },
  "sharedMemory": {
    "name": "Local\\WACCON_SHARED_BUFFER"
  }
}
```

Pass another JSON file as the first argument when needed:

```bat
Waccon.Server.exe production.json
```

## Web controller

Start the server, then open this address on the same machine:

```text
http://127.0.0.1:52469/web/
```

Vite bundles the controller into `/web/controller.js`; browsers never load
`controller-core.js` as a separate module. The C# build and publish targets run
the bundle automatically and publish only the generated controller assets.

The HTTP endpoint serves only the controller files and `GET /health`. `GET /ws`
performs a standard WebSocket upgrade. Browser messages are binary WCON frames:

1. `HELLO` / `WELCOME` during connection setup;
2. a 262-byte `INPUT_SNAPSHOT` after each touch-state change;
3. `CLEAR_INPUT` on page hide, pointer cancellation, or close.

The server writes the configured lease into each accepted snapshot, sends WCON
`LED_SNAPSHOT` frames when output changes, and clears shared-memory input when
the browser connection closes. The default loopback-only address keeps the
controller local; the page currently assumes an empty `authToken`.

The controller displays LED feedback using this verified cabinet layout:

```text
12 modules = 6 visual-left + 6 visual-right
Each module = 5 columns x 4 rows = 20 touch zones
Each module = 5 LED strips x 8 LEDs = 40 LEDs
Each touch zone = 2 LEDs (upper/lower)
Total = 240 touch zones and 480 RGBA LED units
```

The raw LED callback is module-based, not a simple circular `cell * 2`
sequence. The mapping compensates for cabinet wiring: module order is reversed
between halves, the visual left half reverses the five columns inside each
module, both halves reverse the four row/ring positions, and the visual right
half reverses the two LEDs within each zone. In browser code, `side=0` means
visual right (`x >= centerX`) and `side=1` means visual left (`x < centerX`).
Using the post-mapping raw module index to decide left/right caused earlier
attempts to flip the wrong half. The mapping is implemented and tested in
`web/controller-core.js`.

The controller's lower-right status reports whether a compatible `waccon_io.dll`
has entered `mercury_io_poll()`. The same diagnostics are available from
`GET /health`, including `ioConnected`, `ioPid`, and `ioInputSequence`. The
shared-memory ABI is v1.1; update the Server and DLL as a pair.

Set `server.debugInput` to `true` when diagnosing touch input. The console will
log only changed active-cell sets, for example `[input] source=123 activeCells=7,135`.

The protocol uses a 24-byte little-endian WCON header and supports HELLO/WELCOME, INPUT_SNAPSHOT, CLEAR_INPUT, PING/PONG, and LED_SNAPSHOT over TCP and WebSocket. Both transports clear the shared-memory snapshot when a client disconnects.
