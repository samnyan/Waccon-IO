using System.Reflection;
using System.Text.Json;
using DirectN;
using DirectN.Extensions.Com;
using WebView2;
using WebView2.Utilities;

namespace OverlayWebview;

internal sealed class OverlayHost : IDisposable
{
    private const uint WsOverlappedWindow = 0x00cf0000;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExNoRedirectionBitmap = 0x00200000;
    private const nuint RefreshTimerId = 1;
    private const nuint TraySmokeTestTimerId = 2;
    private const nuint SettingsBridgeSmokeTestTimerId = 3;
    private readonly ConfigStore _configStore;
    private readonly NativeWindow _settingsWindow;
    private readonly NativeWindow _overlayWindow;
    private readonly bool _runTrayExitSmokeTest = Environment.GetEnvironmentVariable("OVERLAYWEBVIEW_TRAY_EXIT_SMOKE_TEST") == "1";
    private readonly bool _runSettingsBridgeSmokeTest = Environment.GetEnvironmentVariable("OVERLAYWEBVIEW_SETTINGS_BRIDGE_SMOKE_TEST") == "1";
    private OverlayConfig _config;
    private ICoreWebView2Environment? _environment;
    private SettingsWebView? _settingsWebView;
    private CompositionWebView? _overlayWebView;
    private TrayIcon? _trayIcon;
    private NativeMethods.Rect? _currentGameBounds;
    private bool _overlayVisible = true;
    private bool _trayCallbackHandled;
    private bool _settingsWebViewReady;
    private bool _traySmokeTestScheduled;
    private bool _settingsBridgeSmokeTestScheduled;
    private bool _disposed;

    public OverlayHost()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "overlay_config.json");
        _configStore = new ConfigStore(configPath);
        _config = OverlayConfigNormalizer.Normalize(_configStore.Load());
        DiagnosticLog.Configure(_config.Logging.Enabled);
        _settingsWindow = new NativeWindow("OverlayWebview Settings", WsOverlappedWindow, 0, 720, 680);
        _overlayWindow = new NativeWindow("OverlayWebview", WsPopup, WsExToolWindow | WsExNoActivate | WsExNoRedirectionBitmap, 1, 1);
        _settingsWindow.MessageHandler = HandleSettingsWindowMessage;
        _overlayWindow.MessageHandler = HandleOverlayWindowMessage;
    }

    public void Start()
    {
        WebView2Utilities.Initialize(Assembly.GetEntryAssembly());
        _trayIcon = new TrayIcon(_settingsWindow.Handle, HandleTrayCommand);
        WebView2.Functions.CreateCoreWebView2EnvironmentWithOptions(PWSTR.Null, PWSTR.From(GetUserDataFolder()), null!,
            new CoreWebView2CreateCoreWebView2EnvironmentCompletedHandler(OnEnvironmentCreated)).ThrowOnError();
        _settingsWindow.Show(activate: true);
        NativeMethods.SetTimer(_overlayWindow.Handle, RefreshTimerId, 500, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.KillTimer(_overlayWindow.Handle, RefreshTimerId);
        NativeMethods.KillTimer(_settingsWindow.Handle, TraySmokeTestTimerId);
        NativeMethods.KillTimer(_settingsWindow.Handle, SettingsBridgeSmokeTestTimerId);
        _trayIcon?.Dispose();
        _overlayWebView?.Dispose();
        _settingsWebView?.Dispose();
        _overlayWindow.Dispose();
        _settingsWindow.Dispose();
    }

    private void OnEnvironmentCreated(HRESULT result, ICoreWebView2Environment environment)
    {
        result.ThrowOnError();
        _environment = environment;
        _settingsWebView = new SettingsWebView(_settingsWindow, environment, HandleSettingsMessage);
        _settingsWebView.Ready += (_, _) => OnSettingsWebViewReady();
        if (_settingsWebView.IsReady)
        {
            OnSettingsWebViewReady();
        }
        _overlayWebView = new CompositionWebView(_overlayWindow, environment, _config.Web.Url);
        _overlayWebView.Ready += (_, _) => RefreshOverlay();
        RefreshOverlay();
    }

    private nint? HandleSettingsWindowMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmSize:
                _settingsWebView?.Resize((ushort)(lParam & 0xffff), (ushort)((lParam >> 16) & 0xffff));
                return 0;
            case NativeMethods.WmClose:
                _settingsWindow.Hide();
                return 0;
            case NativeMethods.WmTrayIcon:
                _trayIcon?.HandleCallback((uint)lParam & 0xffff, _overlayVisible);
                _trayCallbackHandled = true;
                return 0;
            case NativeMethods.WmTimer when _runTrayExitSmokeTest && wParam == TraySmokeTestTimerId:
                NativeMethods.KillTimer(_settingsWindow.Handle, TraySmokeTestTimerId);
                if (!_trayCallbackHandled)
                {
                    Environment.ExitCode = 1;
                }

                HandleTrayCommand(TrayCommand.Exit);
                return 0;
            case NativeMethods.WmTimer when _runSettingsBridgeSmokeTest && wParam == SettingsBridgeSmokeTestTimerId:
                NativeMethods.KillTimer(_settingsWindow.Handle, SettingsBridgeSmokeTestTimerId);
                _settingsWebView?.RunBridgeSmokeTest();
                return 0;
        }
        return null;
    }

    private nint? HandleOverlayWindowMessage(uint message, nuint wParam, nint lParam)
    {
        switch (message)
        {
            case NativeMethods.WmTimer when wParam == RefreshTimerId:
                RefreshOverlay();
                return 0;
            case NativeMethods.WmSize:
                _overlayWebView?.SetBounds(new NativeMethods.Rect { Right = (ushort)(lParam & 0xffff), Bottom = (ushort)((lParam >> 16) & 0xffff) });
                return 0;
            case NativeMethods.WmPointerDown:
            case NativeMethods.WmPointerUpdate:
            case NativeMethods.WmPointerUp:
                _overlayWebView?.ForwardPointer(message, wParam);
                return 0;
            case NativeMethods.WmMouseMove:
            case NativeMethods.WmLButtonDown:
            case NativeMethods.WmLButtonUp:
            case NativeMethods.WmRButtonDown:
            case NativeMethods.WmRButtonUp:
            case NativeMethods.WmMouseWheel:
                _overlayWebView?.ForwardMouse(message, wParam, lParam);
                return 0;
        }
        return null;
    }

    private void HandleSettingsMessage(SettingsMessage message)
    {
        try
        {
            DiagnosticLog.Write($"Handling settings message: {message.Type}.");
            if (string.IsNullOrWhiteSpace(message.Type))
            {
                throw new ArgumentException("Settings message type is required.");
            }

            SendSettings(new SettingsResponse("ack", Message: $"Received {message.Type}."));
            switch (message.Type)
            {
                case "getConfig":
                    SendSettings(new SettingsResponse("config", _config));
                    break;
                case "listWindows":
                    SendSettings(new SettingsResponse("windows", Windows: TargetWindowFinder.ListWindows()));
                    break;
                case "saveConfig" when message.Config is not null:
                    var updatedConfig = OverlayConfigNormalizer.Normalize(message.Config);
                    _configStore.Save(updatedConfig);
                    _config = updatedConfig;
                    DiagnosticLog.Configure(updatedConfig.Logging.Enabled);
                    _overlayVisible = true;
                    RefreshOverlay();
                    SendSettings(new SettingsResponse("saved", _config, Message: "Saved and applied."));
                    break;
                case "preview":
                    _overlayVisible = true;
                    RefreshOverlay();
                    break;
                case "hideOverlay":
                    _overlayVisible = false;
                    _overlayWindow.Hide();
                    break;
                case "reload":
                    _overlayWebView?.NotifyParentMoved();
                    break;
                case "exit":
                    RequestExit();
                    break;
                default:
                    throw new ArgumentException($"Unknown settings message type: {message.Type}.");
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Settings command failed: {exception.Message}");
            SendSettings(new SettingsResponse("error", Message: exception.Message));
        }
    }

    private void RefreshOverlay()
    {
        if (!_overlayVisible)
        {
            _overlayWindow.Hide();
            return;
        }

        var target = TargetWindowFinder.Find(_config.Target);
        if (target is null || !TargetWindowFinder.TryGetVisibleBounds((nint)target.Handle, out var gameBounds))
        {
            _currentGameBounds = null;
            _overlayWindow.Hide();
            return;
        }

        _currentGameBounds = gameBounds;
        var margins = _config.Overlay;
        var width = gameBounds.Width + margins.Left + margins.Right;
        var height = gameBounds.Height + margins.Top + margins.Bottom;
        var x = gameBounds.Left - margins.Left + margins.OffsetX;
        var y = gameBounds.Top - margins.Top + margins.OffsetY;
        NativeMethods.SetWindowPos(_overlayWindow.Handle, margins.Topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost, x, y, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoSendChanging);
        _overlayWebView?.SetBounds(new NativeMethods.Rect { Right = width, Bottom = height });
        _overlayWebView?.NotifyParentMoved();
        _overlayWebView?.PostLayout(gameBounds, margins);
    }

    private void SendSettings(SettingsResponse response) => _settingsWebView?.Post(response);

    private void HandleTrayCommand(TrayCommand command)
    {
        switch (command)
        {
            case TrayCommand.OpenSettings:
                _settingsWindow.Show(activate: true);
                NativeMethods.SetForegroundWindow(_settingsWindow.Handle);
                break;
            case TrayCommand.ToggleOverlay:
                _overlayVisible = !_overlayVisible;
                RefreshOverlay();
                break;
            case TrayCommand.Exit:
                RequestExit();
                break;
        }
    }

    private void RequestExit()
    {
        _trayIcon?.Dispose();
        Environment.Exit(0);
    }

    private void ScheduleTraySmokeTest()
    {
        if (!_runTrayExitSmokeTest || !_settingsWebViewReady || _traySmokeTestScheduled)
        {
            return;
        }

        _traySmokeTestScheduled = true;
        var callback = (nint)((1u << 16) | NativeMethods.WmLButtonUp);
        NativeMethods.PostMessage(_settingsWindow.Handle, NativeMethods.WmTrayIcon, 0, callback);
        NativeMethods.SetTimer(_settingsWindow.Handle, TraySmokeTestTimerId, 2000, 0);
    }

    private void OnSettingsWebViewReady()
    {
        if (_settingsWebViewReady)
        {
            return;
        }

        _settingsWebViewReady = true;
        DiagnosticLog.Write("Settings WebView is ready.");
        if (_runSettingsBridgeSmokeTest && !_settingsBridgeSmokeTestScheduled)
        {
            _settingsBridgeSmokeTestScheduled = true;
            NativeMethods.SetTimer(_settingsWindow.Handle, SettingsBridgeSmokeTestTimerId, 1000, 0);
        }

        ScheduleTraySmokeTest();
    }

    private static string GetUserDataFolder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OverlayWebview", "WebView2");
    }
}
