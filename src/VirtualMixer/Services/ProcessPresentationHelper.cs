using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        try
        {
            var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processName))
                .FirstOrDefault();

            if (process is not null)
            {
                using (process)
                {
                    var filePath = process.MainModule?.FileName;
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
            }

            if (ExecutablePathCache.TryGetValue(processName, out var cachedPath))
            {
                return ExtractIconFromFile(cachedPath);
            }
        }
        catch
        {
        }

        return null;
    }

    public static ImageSource? GetProcessIcon(Process process)
    {
        try
        {
            var filePath = process.MainModule?.FileName;
            if (filePath is null)
            {
                return null;
            }

            return ExtractIconFromFile(filePath);
        }
        catch
        {
            return null;
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
                var versionInfo = process.MainModule?.FileVersionInfo;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
