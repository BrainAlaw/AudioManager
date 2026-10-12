using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using AudioManager.Contracts;
using AudioManager.Models;

namespace AudioManager.Services.Input;

public sealed class KeyboardHookService : IKeyboardHookService
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private readonly HashSet<int> _keysDown = [];
    private volatile HashSet<int> _boundKeys = [];
    private volatile bool _captureAllKeys;
    private IntPtr _hookId;

    public KeyboardHookService()
    {
        _proc = HookCallback;
    }

    public event EventHandler<KeyboardKeyEventArgs>? KeyPressed;

    public bool IsRunning => _hookId != IntPtr.Zero;

    public bool CaptureAllKeys
    {
        get => _captureAllKeys;
        set => _captureAllKeys = value;
    }

    public void UpdateBoundKeys(IEnumerable<int> virtualKeys)
    {
        _boundKeys = new HashSet<int>(virtualKeys);
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule is null ? IntPtr.Zero : GetModuleHandle(currentModule.ModuleName);
        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, moduleHandle, 0);
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var message = (int)wParam;
                if (message is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)
                {
                    var virtualKey = Marshal.ReadInt32(lParam);
                    if (message is WmKeyUp or WmSysKeyUp)
                    {
                        _keysDown.Remove(virtualKey);
                    }
                    else
                    {
                        var isRepeat = !_keysDown.Add(virtualKey);
                        if (_captureAllKeys || _boundKeys.Contains(virtualKey))
                        {
                            var key = KeyInterop.KeyFromVirtualKey(virtualKey);
                            var args = new KeyboardKeyEventArgs
                            {
                                VirtualKey = virtualKey,
                                KeyName = key == Key.None ? $"VK {virtualKey}" : key.ToString(),
                                IsRepeat = isRepeat
                            };

                            ThreadPool.QueueUserWorkItem(_ => KeyPressed?.Invoke(this, args));
                        }
                    }
                }
            }
            catch
            {
                // A hook callback must never throw; swallowing keeps the system keyboard path intact.
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
