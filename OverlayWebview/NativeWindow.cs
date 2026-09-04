using DirectN;
using DirectN.Extensions.Utilities;

namespace OverlayWebview;

internal sealed class NativeWindow : Window
{
    public NativeWindow(string title, uint style, uint extendedStyle, int width, int height)
        : base(title, (WINDOW_STYLE)style, (WINDOW_EX_STYLE)extendedStyle, new RECT(100, 100, 100 + width, 100 + height))
    {
    }

    public Func<uint, nuint, nint, nint?>? MessageHandler { get; set; }

    public void Show(bool activate)
    {
        base.Show(activate ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
    }

    protected override LRESULT? WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        var result = MessageHandler?.Invoke(message, wParam.Value, lParam.Value);
        return result.HasValue ? (LRESULT)result.Value : base.WindowProc(hwnd, message, wParam, lParam);
    }
}
