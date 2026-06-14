using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orate.Services;

namespace Orate.Views;

/// <summary>
/// Lets the user rebind the push-to-talk key. Click to arm, then press any key (including a
/// lone modifier like Right Alt). The captured virtual-key code is saved to settings and the
/// live hotkey is updated. Mirrors macOS HotkeyRecorder. The global hook is suppressed while
/// arming so it doesn't fire on the key being chosen.
/// </summary>
public partial class HotkeyRecorderControl : UserControl
{
    private bool _recording;

    public HotkeyRecorderControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        RecordButton.Content = VirtualKeys.DisplayName(SettingsStore.Current.PushToTalkVk);
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (_recording) { StopRecording(); return; }

        _recording = true;
        if (App.Hotkey != null) App.Hotkey.Suppressed = true;
        RecordButton.Content = "Press a key…";
        HintText.Text = "Press the key to use (Esc to cancel)";
        Focus();
        Keyboard.Focus(this);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            StopRecording();
            return;
        }

        SettingsStore.Current.PushToTalkVk = vk;
        SettingsStore.Save();
        if (App.Hotkey != null) App.Hotkey.TargetVk = vk;

        StopRecording();
    }

    private void StopRecording()
    {
        _recording = false;
        if (App.Hotkey != null) App.Hotkey.Suppressed = false;
        HintText.Text = "Click, then press a key to use as push-to-talk";
        UpdateDisplay();
    }
}
