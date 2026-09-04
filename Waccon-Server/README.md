# Waccon Server

C# console bridge between WCON clients and the native `waccon_io.dll` shared-memory ABI.

## Build

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

## Configuration

The server reads `appsettings.json` from its working directory. It defaults to localhost only:

```json
{
  "server": {
    "listenAddress": "127.0.0.1",
    "tcpPort": 52468,
    "maxClients": 4,
    "leaseTimeoutMs": 500,
    "maxPayloadBytes": 4096,
    "authToken": ""
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

The current protocol skeleton uses a 24-byte little-endian WCON header and supports HELLO/WELCOME, INPUT_SNAPSHOT, CLEAR_INPUT, PING/PONG, and LED_SNAPSHOT. TCP input disconnects clear the shared-memory snapshot.
