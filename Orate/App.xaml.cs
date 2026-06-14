using System.Diagnostics;
using System.Windows;
using Orate.Overlay;
using Orate.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Orate;

/// <summary>
/// Application entry point and pipeline orchestrator — the Windows analog of macOS AppDelegate.
/// Owns the tray icon, the global push-to-talk hook, the recorder, and the overlay, and runs
/// the record → transcribe → paste pipeline.
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    /// <summary>Exposed so the settings rebind control can suppress/retarget the live hook.</summary>
    public static GlobalHotkey? Hotkey { get; private set; }

    private OverlayWindow _overlay = null!;
    private AudioRecorder _recorder = null!;
    private GlobalHotkey _hotkey = null!;
    private Forms.NotifyIcon _tray = null!;
    private MainWindow? _mainWindow;

    private CancellationTokenSource? _cts;
    private bool _ownsMutex;
    public string? LastTranscription { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Orate.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }
        _ownsMutex = true;

        _overlay = new OverlayWindow();
        _overlay.ShowOverlay();

        _recorder = new AudioRecorder();
        _recorder.OnLevel = level => _overlay.UpdateLevel(level);

        _hotkey = new GlobalHotkey(SettingsStore.Current.PushToTalkVk);
        _hotkey.PushToTalkDown += OnPushToTalkDown;
        _hotkey.PushToTalkUp += OnPushToTalkUp;
        _hotkey.EscPressed += OnEscPressed;
        _hotkey.Start();
        Hotkey = _hotkey;

        SetupTray();

        ShowMainWindow(); // open on first launch so the user can configure
    }

    // MARK: - Pipeline

    private void OnPushToTalkDown()
    {
        if (_overlay.IsTranscribing) return; // ignore while a transcription is in flight
        _overlay.SetListening(true);
        _recorder.StartRecording();
    }

    private void OnPushToTalkUp()
    {
        if (!_recorder.IsRecording) return;
        _overlay.SetListening(false);
        FinishRecording();
    }

    private void OnEscPressed()
    {
        if (_overlay.IsTranscribing) CancelTranscription();
    }

    private async void FinishRecording()
    {
        var audio = _recorder.StopRecording();
        if (audio == null)
        {
            _overlay.SetListening(false);
            return;
        }

        _overlay.SetTranscribing(true);
        _cts = new CancellationTokenSource();

        try
        {
            var result = await TranscriptionService.TranscribeAsync(audio, _cts.Token);
            _cts.Token.ThrowIfCancellationRequested();

            LastTranscription = result.Transcript;
            if (!string.IsNullOrEmpty(result.Transcript))
            {
                TextInserter.InsertText(result.Transcript);
            }
            Debug.WriteLine($"Transcription inserted ({result.LatencyMs}ms): {result.Transcript}");
            _overlay.SetTranscribing(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user via Esc — overlay already reset in CancelTranscription.
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Transcription failed: {ex}");
            _overlay.ShowError();
        }
        finally
        {
            _cts = null;
        }
    }

    private void CancelTranscription()
    {
        _cts?.Cancel();
        _overlay.SetTranscribing(false);
        Debug.WriteLine("Transcription cancelled by user");
    }

    // MARK: - Tray & window

    private void SetupTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Settings", null, (_, _) =>
        {
            ShowMainWindow();
            _mainWindow?.NavigateToSettings();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Orate",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= new MainWindow();
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }
        _mainWindow.Activate();
    }

    private void QuitApp()
    {
        if (_mainWindow != null) _mainWindow.AllowClose = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
