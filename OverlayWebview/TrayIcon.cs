using System.Runtime.InteropServices;

namespace OverlayWebview;

internal enum TrayCommand
{
    OpenSettings,
    ToggleOverlay,
    Exit,
}

internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint OpenSettingsId = 1;
    private const uint ToggleOverlayId = 2;
    private const uint ExitId = 3;

    private readonly nint _window;
    private readonly Action<TrayCommand> _commandHandler;
    private readonly nint _icon;
    private bool _disposed;

    internal TrayIcon(nint window, Action<TrayCommand> commandHandler)
    {
        _window = window;
        _commandHandler = commandHandler;
        _icon = NativeMethods.LoadIcon(0, NativeMethods.IdiApplication);

        var data = CreateData(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip);
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data))
        {
            throw new InvalidOperationException($"Unable to create the notification icon: {Marshal.GetLastWin32Error()}.");
        }

        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimSetVersion, ref data);
    }

    internal void HandleCallback(uint message, bool overlayVisible)
    {
        switch (message)
        {
            case NativeMethods.WmLButtonUp:
            case NativeMethods.NinSelect:
                _commandHandler(TrayCommand.OpenSettings);
                break;
            case NativeMethods.WmRButtonUp:
            case NativeMethods.WmContextMenu:
                ShowMenu(overlayVisible);
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var data = CreateData(0);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
        _disposed = true;
    }

    private void ShowMenu(bool overlayVisible)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, OpenSettingsId, "Open Settings");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ToggleOverlayId, overlayVisible ? "Hide Overlay" : "Show Overlay");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitId, "Exit");

            NativeMethods.SetForegroundWindow(_window);
            if (!NativeMethods.GetCursorPos(out var point))
            {
                return;
            }

            var selected = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCommand,
                point.X,
                point.Y,
                0,
                _window,
                0);
            NativeMethods.PostMessage(_window, NativeMethods.WmNull, 0, 0);

            switch (selected)
            {
                case OpenSettingsId:
                    _commandHandler(TrayCommand.OpenSettings);
                    break;
                case ToggleOverlayId:
                    _commandHandler(TrayCommand.ToggleOverlay);
                    break;
                case ExitId:
                    _commandHandler(TrayCommand.Exit);
                    break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private NativeMethods.NotifyIconData CreateData(uint flags)
    {
        return new NativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            Window = _window,
            Id = IconId,
            Flags = flags,
            CallbackMessage = NativeMethods.WmTrayIcon,
            Icon = _icon,
            Tip = "OverlayWebview",
            Info = string.Empty,
            InfoTitle = string.Empty,
        };
    }
}
