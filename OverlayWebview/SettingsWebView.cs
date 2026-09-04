using System.Reflection;
using System.Text.Json;
using DirectN;
using DirectN.Extensions.Com;
using WebView2;
using WebView2.Utilities;

namespace OverlayWebview;

internal sealed class SettingsWebView : IDisposable
{
    private readonly NativeWindow _window;
    private readonly ICoreWebView2Environment _environment;
    private readonly Action<SettingsMessage> _messageReceived;
    private ComObject<ICoreWebView2Controller>? _controller;
    private ComObject<ICoreWebView2>? _webView;
    private CoreWebView2WebMessageReceivedEventHandler? _webMessageHandler;
    private EventRegistrationToken _messageToken;
    private bool _disposed;

    public bool IsReady => _controller is not null && _webView is not null;
    public event EventHandler? Ready;

    public SettingsWebView(NativeWindow window, ICoreWebView2Environment environment, Action<SettingsMessage> messageReceived)
    {
        _window = window;
        _environment = environment;
        _messageReceived = messageReceived;
        _environment.CreateCoreWebView2Controller(window.Handle,
            new CoreWebView2CreateCoreWebView2ControllerCompletedHandler(OnControllerCreated)).ThrowOnError();
    }

    public void Resize(int width, int height)
    {
        _controller?.Object.put_Bounds(new RECT(0, 0, width, height)).ThrowOnError();
    }

    public void Post(SettingsResponse response)
    {
        if (_webView is null) return;
        DiagnosticLog.Write($"Posting settings response: {response.Type}.");
        var json = JsonSerializer.Serialize(response, OverlayJsonContext.Default.SettingsResponse);
        _webView.Object.PostWebMessageAsJson(PWSTR.From(json)).ThrowOnError();
    }

    public void RunBridgeSmokeTest()
    {
        DiagnosticLog.Write("Running settings bridge smoke-test script.");
        const string script = """
            (() => {
              const run = () => {
                const bridge = window.chrome && window.chrome.webview;
                if (!bridge) {
                  return;
                }

                let phase = 'save';
                const onMessage = event => {
                  if (event.data.type === 'saved' && phase === 'save') {
                    phase = 'preview';
                    bridge.postMessage({ type: 'preview' });
                  } else if (event.data.type === 'ack' && phase === 'preview') {
                    phase = 'hide';
                    bridge.postMessage({ type: 'hideOverlay' });
                  } else if (event.data.type === 'ack' && phase === 'hide') {
                    bridge.removeEventListener('message', onMessage);
                    phase = 'exit';
                    bridge.postMessage({ type: 'exit' });
                  }
                };
                bridge.addEventListener('message', onMessage);

                bridge.postMessage({
                  type: 'saveConfig',
                  config: {
                    target: { processName: '', titlePattern: '', matchMode: 'contains' },
                    web: { url: 'http://127.0.0.1:52469/web/' },
                    overlay: { left: 0, top: 0, right: 0, bottom: 0, offsetX: 0, offsetY: 0, topmost: true }
                  }
                });
              };

              if (document.readyState === 'loading') {
                document.addEventListener('DOMContentLoaded', run, { once: true });
              } else {
                run();
              }
            })();
            """;

        if (_webView is null)
        {
            throw new InvalidOperationException("Settings WebView is not ready.");
        }

        _webView.Object.ExecuteScript(PWSTR.From(script), new CoreWebView2ExecuteScriptCompletedHandler((result, _) => result.ThrowOnError())).ThrowOnError();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_messageToken.value != 0) _webView?.Object.remove_WebMessageReceived(_messageToken);
        _webMessageHandler = null;
        _controller?.Object.Close();
        _webView?.Dispose();
        _controller?.Dispose();
    }

    private void OnControllerCreated(HRESULT result, ICoreWebView2Controller controller)
    {
        DiagnosticLog.Write("Settings WebView controller created.");
        result.ThrowOnError();
        _controller = new ComObject<ICoreWebView2Controller>(controller);
        controller.put_Bounds(new RECT(0, 0, 720, 680)).ThrowOnError();
        controller.get_CoreWebView2(out var webView).ThrowOnError();
        _webView = new ComObject<ICoreWebView2>(webView);
        webView.get_Settings(out var settings).ThrowOnError();
        settings.put_IsWebMessageEnabled(true).ThrowOnError();
        _webMessageHandler = new CoreWebView2WebMessageReceivedEventHandler(OnWebMessage);
        webView.add_WebMessageReceived(_webMessageHandler, ref _messageToken).ThrowOnError();
        DiagnosticLog.Write("Settings WebView message handler registered.");
        webView.NavigateToString(PWSTR.From(SettingsPage.Html));
        Ready?.Invoke(this, EventArgs.Empty);
    }

    private void OnWebMessage(ICoreWebView2 sender, ICoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            if (args.get_WebMessageAsJson(out var payload).IsError) return;
            var json = payload.ToStringAndDispose();
            if (string.IsNullOrWhiteSpace(json)) return;
            var message = JsonSerializer.Deserialize(json, OverlayJsonContext.Default.SettingsMessage);
            if (message is not null)
            {
                DiagnosticLog.Write($"Received settings message: {message.Type}.");
                _messageReceived(message);
            }
        }
        catch (Exception exception)
        {
            DiagnosticLog.Write($"Settings message failed: {exception.Message}");
            Post(new SettingsResponse("error", Message: exception.Message));
        }
    }

}
