using System.Windows;
using System.Windows.Threading;
using Orate.Interop;

namespace Orate.Services;

/// <summary>
/// Inserts text into the focused app by stashing the clipboard, setting our text, sending
/// Ctrl+V, then restoring the clipboard. Windows analog of macOS TextInserter (which used
/// Cmd+V via CGEvent). No accessibility permission is required on Windows.
/// Must be called on the UI (STA) thread — the clipboard API requires it.
/// </summary>
public static class TextInserter
{
    public static void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var dispatcher = Application.Current.Dispatcher;
        string? previous = TryGetClipboardText();

        if (!TrySetClipboardText(text))
        {
            System.Diagnostics.Debug.WriteLine("Failed to set clipboard; aborting paste");
            return;
        }

        SendCtrlV();

        // Restore the previous clipboard once the paste has been processed.
        Task.Delay(200).ContinueWith(_ =>
        {
            dispatcher.Invoke(() =>
            {
                if (previous != null) TrySetClipboardText(previous);
                else TryClearClipboard();
            });
        }, TaskScheduler.Default);
    }

    private static string? TryGetClipboardText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySetClipboardText(string text)
    {
        // The Win32 clipboard is shared and frequently briefly locked by other apps; retry.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                return true;
            }
            catch
            {
                System.Threading.Thread.Sleep(20);
            }
        }
        return false;
    }

    private static void TryClearClipboard()
    {
        try { Clipboard.Clear(); } catch { /* ignore */ }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            KeyInput(NativeMethods.VK_CONTROL, keyUp: false),
            KeyInput(NativeMethods.VK_V, keyUp: false),
            KeyInput(NativeMethods.VK_V, keyUp: true),
            KeyInput(NativeMethods.VK_CONTROL, keyUp: true),
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT KeyInput(ushort vk, bool keyUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
            },
        },
    };
}
