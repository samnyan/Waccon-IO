# Waccon waccon-io

`waccon_io.dll` is a custom IO provider for the segatools WACCA (`mercury`) hook.

Phase 1 implements the complete mercuryio API 1.0 surface and a thread-safe minimal state core. Providers are intentionally not included yet; the initial module uses safe neutral defaults so it can be loaded before later input backends are enabled.

## Build with the batch script

From this directory:

```bat
build.bat build
build.bat test
build.bat project
build.bat clean
```

The default release artifact is:

```text
build\waccon_io.dll
```

The script detects Visual Studio with `vswhere`, initializes the x64 MSVC environment, and uses the Ninja Meson backend. If Meson is not in `PATH`, install it with `python -m pip install meson ninja`.


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

The module locates the game window inside the injected/current process. It first tries the known WACCA title `Mercury  `, then `WACCA`, and finally enumerates visible top-level windows owned by the current process while excluding `ConsoleWindowClass`. Touch and mouse coordinates are relative to that window's client area; they are not assumed to be centered on the desktop.

Copy `waccon.ini.example` settings into the `[waccon]` section of `segatools.ini`:

```ini
[waccon]
cursor=1    ; force the game cursor visible (default 1)
wintouch=1  ; enable WM_TOUCH provider (default 1)
mouse=1     ; enable left-button mouse fallback (default 1)
```

`cursor=1` periodically balances the game's `ShowCursor(FALSE)` calls and restores the standard arrow cursor. Set it to `0` if the game or another overlay must control cursor visibility.

The standard segatools `[io4]` keyboard bindings are also supported directly by this DLL. They are sampled when the game reads the operator or game buttons, so they do not depend on `mercury_io_poll()` being called:

```ini
[io4]
test=0x70     ; F1
service=0x71  ; F2
coin=0x72     ; F3
volup=0x26    ; Up Arrow
voldown=0x28  ; Down Arrow
```
