# Waccon IO

English | [中文](README_CN.md)

Waccon IO is a multi-purpose controller implementation for WACCA.

Most existing projects of this kind rely on serial port emulation. However, virtual serial port solutions such as com0com are no longer very practical on Windows 11.

Waccon IO is instead implemented as a custom `mercuryio` module for segatools. It does not require any virtual serial ports and also includes full LED output support.

Currently supported input methods:

* WinTouch touchscreen and mouse input
* Web-based virtual controller

## Usage

### Basic Setup

Copy `waccon_io.dll` into the same directory as segatools, then edit `segatools.ini` and set the `path` option under `[mercuryio]` to `waccon_io.dll`.

The final configuration should look like this:

```ini
[mercuryio]
path=waccon_io.dll
```

### Touchscreen and Mouse Input

Add a `[waccon]` section to `segatools.ini` and configure it as shown below:

```ini
[waccon]
; Show mouse cursor
cursor=1

; Enable WinTouch touchscreen input
wintouch=1

; Enable mouse input
mouse=1

; Window title used to locate the game window
windowTitle=Mercury

; Center position in normalized screen coordinates
; Slightly above the center of the screen works well by default
centerX=0.50
centerY=0.47

; Outer radius
; Relative to the window width, where 1.0 means 100% of the width
radius=1

; Inner radius
; The input area is a ring, in this case from 60% to 100% of the configured radius
innerRadius=0.60

startAngle=-90
reverse=0
```

### Virtual Controller

A web-based virtual controller is currently supported.

Run `Waccon.Server.exe`, then open the address shown in the console to access the virtual controller.

The listening port and other settings can be changed in `appsettings.json`.

### FAQ

* **Q: The LED status shows `Error` on startup.**

  * **A:** `ftd2xx.dll` is missing. Place it in the `WindowsNoEditor\Mercury\Binaries\Win64` directory.

## Project Structure

* `waccon-io/` — The `mercuryio` module. Provides basic functionality such as touchscreen and mouse input, and exposes I/O data to external applications through shared memory.
* `Waccon-Server/` — A virtual controller server written in C#. It communicates with `waccon-io` through shared memory and exposes the I/O data through protocols such as WebSocket and UDP for external virtual controllers.

## Acknowledgements

This project was developed with reference to and inspiration from the following projects:

* [toucca](https://github.com/BlueGlassBlock/toucca) by BlueGlassBlock
* Any2WACCAi by Raymonf
* Any2WACCA_with_WACCAVCon by Mishe.W#7250
* [Brokenithm-iOS](https://github.com/esterTion/Brokenithm-iOS) by esterTion

**Note: This project was developed with the assistance of AI.**
