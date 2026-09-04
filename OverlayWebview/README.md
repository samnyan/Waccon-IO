# OverlayWebview

A small, generic Windows overlay host for an arbitrary web page. It uses native
Win32 windows, WebView2Aot, DirectComposition, and a local `overlay_config.json`.

The target process is optional. Configure either `target.processName`,
`target.titlePattern`, or both. The overlay follows the target's visible bounds,
and expands by the configured margins. Its WebView background is transparent,
so transparent HTML shows the target application underneath while opaque page
elements render as the overlay.

`overlay_config.json` is read next to the executable. The supplied example points to
the Waccon controller only as a convenient local default; replace `web.url` for
any other controller page.

## Layout

All layout values are pixels. For a target window at `(targetX, targetY)` with
size `(targetWidth, targetHeight)`, the overlay window is placed as follows:

```text
overlayX = targetX - left + offsetX
overlayY = targetY - top + offsetY
overlayWidth = targetWidth + left + right
overlayHeight = targetHeight + top + bottom
```

`left`, `top`, `right`, and `bottom` expand the WebView around the target. A
positive `offsetX` moves the entire overlay right; a negative `offsetY` moves
it up. With the supplied defaults, the overlay starts 320 pixels left and 222
pixels above the target, and is 640 pixels wider and 360 pixels taller.

## Build

```bat
dotnet build OverlayWebview.csproj --disable-build-servers -m:1
```

To publish and run the startup smoke test, including a configuration with null
nested sections, a simulated notification-area callback followed by `Exit`,
and a JavaScript-to-host `Save & Apply` round trip:

```bat
dotnet publish OverlayWebview.csproj -c Release -r win-x64 -p:PublishAot=true -p:RunStartupSmokeTest=true --disable-build-servers -m:1
```

WebView2 Runtime must be installed. The settings window opens on startup and
writes validated changes back to the local `overlay_config.json`. Set `logging.enabled`
to `true` (or select **Enable diagnostic log** in Settings) to create
`OverlayWebview.log`; it is disabled by default. Closing that window
keeps the host running in the notification area. Left-click its tray icon to
reopen settings, or right-click it for `Show Overlay` / `Hide Overlay` and
`Exit`.
