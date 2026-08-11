using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text;

namespace AudioManager.Services;

public static class ProcessPresentationHelper
{
    private const uint ShgfiIcon = 0x000000100;
    private static readonly Dictionary<string, string> ExecutablePathCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetFriendlyName(string processName, string? sessionDisplayName = null)
    {
        var fromProcess = TryGetFriendlyNameFromRunningProcess(processName);
        if (!string.IsNullOrWhiteSpace(fromProcess))
        {
            return fromProcess;
        }

        if (!string.IsNullOrWhiteSpace(sessionDisplayName))
        {
            var cleaned = CleanDisplayName(sessionDisplayName, processName);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                return cleaned;
            }
        }

        return Path.GetFileNameWithoutExtension(processName);
    }

    public static Action<string, string>? OnPathResolved { get; set; }

    public static void CachePath(string processName, string executablePath)
    {
        ExecutablePathCache[processName] = executablePath;
    }

    public static ImageSource? GetProcessIcon(string processName)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName)))
        {
            using (process)
            {
                try
                {
                    var filePath = TryGetExecutablePath(process);
                    if (filePath is not null)
                    {
                        ExecutablePathCache[processName] = filePath;
                        OnPathResolved?.Invoke(processName, filePath);
                    }

                    var icon = GetProcessIcon(process);
                    if (icon is not null)
                    {
                        return icon;
                    }
                }
                catch
                {
                }
            }
        }

        try
        {
            if (ExecutablePathCache.TryGetValue(processName, out var cachedPath))
            {
                var cachedIcon = ExtractIconFromFile(cachedPath);
                if (cachedIcon is not null)
                {
                    return cachedIcon;
                }
            }
        }
        catch
        {
        }

        return CreatePlaceholderIcon(processName);
    }

    public static ImageSource? GetProcessIcon(Process process)
    {
        try
        {
            var filePath = TryGetExecutablePath(process);
            if (filePath is null)
            {
                return CreatePlaceholderIcon(process.ProcessName);
            }

            var icon = ExtractIconFromFile(filePath);
            return icon ?? CreatePlaceholderIcon(process.ProcessName);
        }
        catch
        {
            return CreatePlaceholderIcon(process.ProcessName);
        }
    }

    public static string? ResolveExecutablePath(string processName)
    {
        ExecutablePathCache.TryGetValue(processName, out var cached);
        return cached;
    }

    private static ImageSource? ExtractIconFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var info = new ShFileInfo();
        var result = SHGetFileInfo(
            filePath,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon);

        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(32, 32));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    private static ImageSource CreatePlaceholderIcon(string processName)
    {
        const int size = 64;
        var visual = new DrawingVisual();
        var label = Path.GetFileNameWithoutExtension(processName)
            .Where(char.IsLetterOrDigit)
            .Take(2)
            .Aggregate(string.Empty, (current, character) => current + char.ToUpperInvariant(character));

        if (string.IsNullOrWhiteSpace(label))
        {
            label = "?";
        }

        using (var context = visual.RenderOpen())
        {
            var background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 45));
            var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 6, 0));
            var text = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                24,
                System.Windows.Media.Brushes.White,
                VisualTreeHelper.GetDpi(visual).PixelsPerDip);

            context.DrawRoundedRectangle(background, new System.Windows.Media.Pen(accent, 2), new Rect(0, 0, size, size), 12, 12);
            context.DrawText(text, new System.Windows.Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static string? TryGetFriendlyNameFromRunningProcess(string processName)
    {
        try
        {
            var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName))
                .FirstOrDefault();

            if (process is null)
            {
                return null;
            }

            using (process)
            {
                var filePath = TryGetExecutablePath(process);
                if (filePath is not null)
                {
                    ExecutablePathCache[processName] = filePath;
                    OnPathResolved?.Invoke(processName, filePath);
                }

                var versionInfo = filePath is null
                    ? null
                    : FileVersionInfo.GetVersionInfo(filePath);
                return CleanDisplayName(versionInfo?.FileDescription, processName)
                       ?? CleanDisplayName(versionInfo?.ProductName, processName);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? CleanDisplayName(string? value, string processName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Microsoft ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            var buffer = new StringBuilder(1024);
            var length = (uint)buffer.Capacity;
            var handle = process.Handle;
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            if (QueryFullProcessImageName(handle, 0, buffer, ref length) && length > 0)
            {
                return buffer.ToString(0, (int)length);
            }
        }
        catch
        {
        }

        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        int dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
