using Orate.Interop;

namespace Orate.Services;

/// <summary>
/// Global push-to-talk via a low-level keyboard hook (WH_KEYBOARD_LL). Fires
/// <see cref="PushToTalkDown"/> when the configured key is first pressed and
/// <see cref="PushToTalkUp"/> when released. Also surfaces Esc via <see cref="EscPressed"/>.
///
/// We use a hook rather than RegisterHotKey because RegisterHotKey only signals a press,
/// not the hold/release that push-to-talk needs. Callbacks arrive on the UI thread
/// (the thread that installed the hook and pumps messages).
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int VK_ESCAPE = 0x1B;

    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _isPressed;

    /// <summary>Virtual-key code that triggers push-to-talk (e.g. 0xA5 = Right Alt).</summary>
    public int TargetVk { get; set; }

    /// <summary>While true the hook ignores the target key — used during rebinding.</summary>
    public bool Suppressed { get; set; }

    public event Action? PushToTalkDown;
    public event Action? PushToTalkUp;
    public event Action? EscPressed;

    public GlobalHotkey(int targetVk)
    {
        TargetVk = targetVk;
        _proc = HookCallback; // keep a strong ref so the delegate isn't GC'd
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        var hMod = NativeMethods.GetModuleHandle(null);
        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("Failed to install keyboard hook");
        }
    }

    public void Stop()
    {
        if (_hookId == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _isPressed = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = System.Runtime.InteropServices.Marshal
                .PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            int vk = (int)info.vkCode;

            bool isDown = msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            bool isUp = msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;

            if (!Suppressed && vk == TargetVk)
            {
                if (isDown && !_isPressed)
                {
                    _isPressed = true;
                    PushToTalkDown?.Invoke();
                }
                else if (isUp && _isPressed)
                {
                    _isPressed = false;
                    PushToTalkUp?.Invoke();
                }
            }
            else if (isDown && vk == VK_ESCAPE)
            {
                EscPressed?.Invoke();
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
