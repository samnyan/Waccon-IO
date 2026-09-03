# Waccon waccon-io

`waccon_io.dll` is a custom IO provider for the segatools WACCA (`mercury`) hook.

Phase 1 implements the complete mercuryio API 1.0 surface and a thread-safe minimal state core. Providers are intentionally not included yet; the initial module uses safe neutral defaults so it can be loaded before later input backends are enabled.

## Build with Meson

From this directory:

```powershell
meson setup build --backend ninja
meson compile -C build
meson test -C build --print-errorlogs
```

The DLL exports the same symbols as `segatools/games/mercuryio/mercuryio.def` and should be selected in the game's configuration with:

```ini
[mercuryio]
path=path\\to\\waccon_io.dll
```

The module does not replace `mercuryhook.dll` and does not require com0com.
