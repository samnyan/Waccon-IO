using System.Diagnostics;
using System.Text;

namespace OverlayWebview;

internal static class TargetWindowFinder
{
    public static IReadOnlyList<WindowCandidate> ListWindows()
    {
        var windows = new List<WindowCandidate>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle)) return true;
            var length = NativeMethods.GetWindowTextLength(handle);
            if (length == 0) return true;

            var title = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, title, title.Capacity);
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new WindowCandidate((long)handle, (int)processId, process.ProcessName, title.ToString()));
            }
            catch (ArgumentException)
            {
            }
            return true;
        }, 0);
        return windows.OrderBy(candidate => candidate.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static WindowCandidate? Find(TargetConfig target)
    {
        if (string.IsNullOrWhiteSpace(target.ProcessName) && string.IsNullOrWhiteSpace(target.TitlePattern)) return null;
        return ListWindows().FirstOrDefault(candidate => Matches(candidate, target));
    }

    public static bool TryGetVisibleBounds(nint handle, out NativeMethods.Rect bounds)
    {
        var hasDwmBounds = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaExtendedFrameBounds, out bounds, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.Rect>()) == 0;
        if (!hasDwmBounds && !NativeMethods.GetWindowRect(handle, out bounds))
        {
            return false;
        }

        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool Matches(WindowCandidate candidate, TargetConfig target)
    {
        if (!string.IsNullOrWhiteSpace(target.ProcessName)
            && !candidate.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(target.TitlePattern)) return true;

        return target.MatchMode.Equals("exact", StringComparison.OrdinalIgnoreCase)
            ? candidate.Title.Equals(target.TitlePattern, StringComparison.OrdinalIgnoreCase)
            : candidate.Title.Contains(target.TitlePattern, StringComparison.OrdinalIgnoreCase);
    }
}
