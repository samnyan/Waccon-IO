using System.Reflection;
using System.Text.Json;
using DirectN;
using DirectN.Extensions;
using DirectN.Extensions.Com;
using WebView2;
using WebView2.Utilities;

namespace OverlayWebview;

internal sealed class CompositionWebView : IDisposable
{
    private const string TransparentDocumentScript = """
        (() => {
          const applyTransparency = () => {
            const roots = [document.documentElement, document.body];
            for (const root of roots) {
              if (!root) {
                continue;
              }

              root.style.setProperty('background', 'transparent', 'important');
              root.style.setProperty('background-color', 'transparent', 'important');
            }
          };

          applyTransparency();
          document.addEventListener('DOMContentLoaded', applyTransparency, { once: true });
        })();
        """;

    private readonly NativeWindow _window;
    private readonly string _url;
    private readonly ICoreWebView2Environment _environment;
    private readonly IComObject<IDCompositionDevice> _compositionDevice;
    private readonly IComObject<IDCompositionTarget> _compositionTarget;
    private readonly IComObject<IDCompositionVisual> _rootVisual;
    private ComObject<ICoreWebView2CompositionController>? _controller;
    private ComObject<ICoreWebView2>? _webView;
    private CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler? _transparentDocumentScriptHandler;
    private NativeMethods.Rect _bounds;
    private bool _disposed;

    public CompositionWebView(NativeWindow window, ICoreWebView2Environment environment, string url)
    {
        _window = window;
        _environment = environment;
        _url = url;

        var deviceId = typeof(IDCompositionDevice).GUID;
        DirectN.Functions.DCompositionCreateDevice2(0, in deviceId, out var device).ThrowOnError();
        if (device == 0) throw new InvalidOperationException("DirectComposition did not return a device.");
        _compositionDevice = ComObject.FromPointer<IDCompositionDevice>(device, System.Runtime.InteropServices.CreateObjectFlags.None, releaseOnDispose: true)
            ?? throw new InvalidOperationException("Unable to wrap the DirectComposition device.");
        _compositionDevice.Object.CreateTargetForHwnd(window.Handle, true, out var target).ThrowOnError();
        if (target is null) throw new InvalidOperationException("DirectComposition did not return a target.");
        _compositionTarget = new ComObject<IDCompositionTarget>(target);
        _compositionDevice.Object.CreateVisual(out var visual).ThrowOnError();
        if (visual is null) throw new InvalidOperationException("DirectComposition did not return a visual.");
        _rootVisual = new ComObject<IDCompositionVisual>(visual);
        _compositionTarget.Object.SetRoot(_rootVisual.Object).ThrowOnError();
        _compositionDevice.Object.Commit().ThrowOnError();

        var environment3 = (ICoreWebView2Environment3)_environment;
        environment3.CreateCoreWebView2CompositionController(window.Handle,
            new CoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler(OnControllerCreated)).ThrowOnError();
    }

    public bool IsReady => _controller is not null && _webView is not null;
    public event EventHandler? Ready;

    public void SetBounds(NativeMethods.Rect bounds)
    {
        _bounds = bounds;
        if (_controller?.Object is ICoreWebView2Controller controller)
        {
            controller.put_Bounds(new RECT(0, 0, bounds.Width, bounds.Height)).ThrowOnError();
        }
    }

    public void NotifyParentMoved()
    {
        if (_controller?.Object is ICoreWebView2Controller controller) controller.NotifyParentWindowPositionChanged().ThrowOnError();
    }

    public void ForwardPointer(uint message, nuint wParam)
    {
        if (_controller is null) return;
        if (message is not (NativeMethods.WmPointerDown or NativeMethods.WmPointerUpdate or NativeMethods.WmPointerUp)) return;

        var controller4 = _controller.As<ICoreWebView2ExperimentalCompositionController4>();
        if (controller4 is null) return;
        var pointerId = (uint)(wParam & 0xffff);
        var matrix = D2D_MATRIX_4X4_F.Identity();
        if (controller4.Object.CreateCoreWebView2PointerInfoFromPointerId(pointerId, _window.Handle, matrix, out var pointer).IsError) return;
        using var pointerInfo = new ComObject<ICoreWebView2PointerInfo>(pointer);
        _controller.Object.SendPointerInput((COREWEBVIEW2_POINTER_EVENT_KIND)message, pointerInfo.Object).ThrowOnError();
    }

    public void ForwardMouse(uint message, nuint wParam, nint lParam)
    {
        if (_controller is null) return;
        var point = new POINT((short)(lParam & 0xffff), (short)((lParam >> 16) & 0xffff));
        var keys = COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS.COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE;
        var kind = message switch
        {
            NativeMethods.WmMouseMove => COREWEBVIEW2_MOUSE_EVENT_KIND.COREWEBVIEW2_MOUSE_EVENT_KIND_MOVE,
            NativeMethods.WmLButtonDown => COREWEBVIEW2_MOUSE_EVENT_KIND.COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_DOWN,
            NativeMethods.WmLButtonUp => COREWEBVIEW2_MOUSE_EVENT_KIND.COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_UP,
            NativeMethods.WmRButtonDown => COREWEBVIEW2_MOUSE_EVENT_KIND.COREWEBVIEW2_MOUSE_EVENT_KIND_RIGHT_BUTTON_DOWN,
            NativeMethods.WmRButtonUp => COREWEBVIEW2_MOUSE_EVENT_KIND.COREWEBVIEW2_MOUSE_EVENT_KIND_RIGHT_BUTTON_UP,
            NativeMethods.WmMouseWheel => COREWEBVIEW2_MOUSE_EVENT_KIND.COREWEBVIEW2_MOUSE_EVENT_KIND_WHEEL,
            _ => (COREWEBVIEW2_MOUSE_EVENT_KIND)(-1)
        };
        if ((int)kind < 0) return;
        var data = message == NativeMethods.WmMouseWheel ? unchecked((uint)(short)((wParam >> 16) & 0xffff)) : 0;
        _controller.Object.SendMouseInput(kind, keys, data, point).ThrowOnError();
    }

    public void PostLayout(NativeMethods.Rect gameBounds, OverlayMargins margins)
    {
        if (_webView is null) return;
        var message = JsonSerializer.Serialize(new OverlayLayoutMessage(gameBounds.Width, gameBounds.Height, margins.Left, margins.Top, margins.Right, margins.Bottom, margins.OffsetX, margins.OffsetY), OverlayJsonContext.Default.OverlayLayoutMessage);
        _webView.Object.PostWebMessageAsJson(PWSTR.From(message)).ThrowOnError();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _webView?.Dispose();
        _controller?.Dispose();
        _rootVisual.Dispose();
        _compositionTarget.Dispose();
        _compositionDevice.Dispose();
    }

    private void OnControllerCreated(HRESULT result, ICoreWebView2CompositionController controller)
    {
        result.ThrowOnError();
        _controller = new ComObject<ICoreWebView2CompositionController>(controller);
        var rootVisual = _rootVisual.As<IUnknown>() ?? throw new InvalidOperationException("Unable to access the composition root visual.");
        _controller.Object.put_RootVisualTarget(rootVisual.Object).ThrowOnError();
        _compositionDevice.Object.Commit().ThrowOnError();

        var baseController = (ICoreWebView2Controller)controller;
        _controller.As<ICoreWebView2Controller2>()?.Object.put_DefaultBackgroundColor(new COREWEBVIEW2_COLOR { A = 0, R = 0, G = 0, B = 0 }).ThrowOnError();
        baseController.put_Bounds(new RECT(0, 0, _bounds.Width, _bounds.Height)).ThrowOnError();
        baseController.put_IsVisible(true).ThrowOnError();
        baseController.get_CoreWebView2(out var webView).ThrowOnError();
        _webView = new ComObject<ICoreWebView2>(webView);
        _transparentDocumentScriptHandler = new CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler((result, _) =>
        {
            if (result.IsError)
            {
                DiagnosticLog.Write($"Transparent document script registration failed: {result}.");
                return;
            }

            DiagnosticLog.Write("Transparent document script registered.");
        });
        webView.AddScriptToExecuteOnDocumentCreated(PWSTR.From(TransparentDocumentScript), _transparentDocumentScriptHandler).ThrowOnError();
        webView.Navigate(PWSTR.From(_url));
        Ready?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed record OverlayLayoutMessage(int GameWidth, int GameHeight, int Left, int Top, int Right, int Bottom, int OffsetX, int OffsetY);
