using System.Diagnostics;
using System.Windows;
using Orate.Overlay;
using Orate.Services;
using Velopack;
using Velopack.Sources;
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

    // Cross-process signal: a second launch sets this so the already-running instance pops its
    // window back up (instead of the second process exiting silently and leaving the user stuck).
    private const string ShowWindowEventName = "Orate.ShowWindow";
    private EventWaitHandle? _showWindowEvent;
    private Thread? _showWindowListener;
    private volatile bool _shuttingDown;

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

    // Update state, surfaced to the Settings screen.
    private UpdateManager? _updateManager;
    private bool _updateInProgress;
    public static App Instance => (App)Current;
    public string CurrentVersionDisplay { get; private set; } = "portable build";
    public event Action? UpdateStateChanged;

    private const string RepoUrl = "https://github.com/victorchrollo14/orate-windows";

    protected override void OnStartup(StartupEventArgs e)
    {
        // MUST be the first thing that runs — Velopack's install/update/uninstall hooks are
        // launched as a brief second process and exit before the UI (or the mutex below) runs.
        VelopackApp.Build().Run();

        base.OnStartup(e);

        // Single instance. A second launch signals the running instance to show its window,
        // then exits — otherwise the user (whose window is hidden to the tray) gets stuck.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Orate.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowWindowEventName).Set();
            }
            catch (Exception ex)
            {
                Logger.Log("Failed to signal existing instance", ex);
            }
            Shutdown();
            return;
        }
        _ownsMutex = true;

        Logger.Log("=== Orate starting ===");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Log("Unhandled exception", (args.ExceptionObject as Exception)!);
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log("Dispatcher unhandled exception", args.Exception);
            args.Handled = true;
        };

        StartShowWindowListener();

        _overlay = new OverlayWindow();
        _overlay.ShowOverlay();

        AudioRecorder.LogFlacAvailability(); // up front: if 0 codecs, that's the whole problem

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

        InitVersionDisplay(); // show the installed version in Settings (no network, no auto-update)
    }

    /// <summary>Reads the installed version for display. No network call, no auto-download.</summary>
    private void InitVersionDisplay()
    {
        try
        {
            var mgr = GetUpdateManager();
            if (mgr.IsInstalled && mgr.CurrentVersion != null)
            {
                CurrentVersionDisplay = mgr.CurrentVersion.ToString();
                UpdateStateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Update: version init failed", ex);
        }
    }

    private UpdateManager GetUpdateManager() =>
        _updateManager ??= new UpdateManager(new GithubSource(RepoUrl, null, prerelease: false));

    /// <summary>
    /// User-initiated update check (tray menu / Settings), mirroring the macOS
    /// "Check for Updates…" flow: check, then prompt before downloading & restarting.
    /// </summary>
    public async Task CheckForUpdatesInteractiveAsync()
    {
        if (_updateInProgress) return;
        _updateInProgress = true;
        try
        {
            UpdateManager mgr;
            try
            {
                mgr = GetUpdateManager();
            }
            catch (Exception ex)
            {
                Logger.Log("Update: manager init failed", ex);
                ShowUpdateInfo("Couldn't check for updates right now. Please try again later.",
                    MessageBoxImage.Warning);
                return;
            }

            if (!mgr.IsInstalled)
            {
                ShowUpdateInfo(
                    "Updates are only available in the installed version of Orate.\n\n"
                    + "Download the latest Setup.exe from the GitHub Releases page to update.",
                    MessageBoxImage.Information);
                return;
            }

            CurrentVersionDisplay = mgr.CurrentVersion?.ToString() ?? "unknown";
            UpdateStateChanged?.Invoke();

            UpdateInfo? info;
            try
            {
                info = await mgr.CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                Logger.Log("Update: check failed", ex);
                ShowUpdateInfo("Couldn't reach the update server. Check your connection and try again.",
                    MessageBoxImage.Warning);
                return;
            }

            if (info == null)
            {
                Logger.Log($"Update: already up to date (v{CurrentVersionDisplay}).");
                ShowUpdateInfo($"You're up to date.\n\nOrate {CurrentVersionDisplay} is the latest version.",
                    MessageBoxImage.Information);
                return;
            }

            var newVersion = info.TargetFullRelease.Version;
            var choice = MessageBox.Show(
                $"Orate {newVersion} is available — you have {CurrentVersionDisplay}.\n\n"
                + "Download and install it now? Orate will restart to finish.",
                "Update Available", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (choice != MessageBoxResult.OK)
            {
                Logger.Log($"Update: {newVersion} available; user postponed.");
                return;
            }

            try
            {
                Logger.Log($"Update: downloading {newVersion} (user-initiated)…");
                await mgr.DownloadUpdatesAsync(info);
                if (_mainWindow != null) _mainWindow.AllowClose = true;
                Logger.Log("Update: applying and restarting.");
                mgr.ApplyUpdatesAndRestart(info.TargetFullRelease);
            }
            catch (Exception ex)
            {
                Logger.Log("Update: download/apply failed", ex);
                ShowUpdateInfo("The update couldn't be installed. Please try again later.",
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _updateInProgress = false;
        }
    }

    private static void ShowUpdateInfo(string text, MessageBoxImage icon) =>
        MessageBox.Show(text, "Orate", MessageBoxButton.OK, icon);

    // MARK: - Pipeline

    private void OnPushToTalkDown()
    {
        if (_overlay.IsTranscribing) return; // ignore while a transcription is in flight
        _overlay.SetListening(true);
        // Open the mic on a background thread — waveInOpen can block ~500ms on the first
        // record, which would otherwise freeze the just-shown waveform into a static rectangle.
        Task.Run(_recorder.StartRecording);
    }

    private void OnPushToTalkUp()
    {
        if (!_recorder.IsRecording) return;
        // Don't drop to idle here — FinishRecording transitions waveform → loading directly,
        // so the pill never flashes its tiny idle state in between.
        FinishRecording();
    }

    private void OnEscPressed()
    {
        if (_overlay.IsTranscribing) CancelTranscription();
    }

    private async void FinishRecording()
    {
        var audio = _recorder.StopRecording();
        if (audio == null || audio.Length == 0)
        {
            // Nothing captured, or FLAC encoding failed — surface it instead of failing silently.
            Logger.Log("FinishRecording: no audio produced (capture empty or FLAC encode failed).");
            _overlay.ShowError(); // resets listening state and shows the error pill
            return;
        }

        Logger.Log($"FinishRecording: {audio.Length} bytes of FLAC; provider={SettingsStore.Current.Provider}");
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
                RecordingStore.Save(audio, result);
                Logger.Log($"Transcription inserted ({result.LatencyMs}ms, {result.Transcript.Length} chars).");
            }
            else
            {
                Logger.Log($"Transcription returned empty text ({result.LatencyMs}ms) — likely silence.");
            }
            _overlay.SetTranscribing(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user via Esc — overlay already reset in CancelTranscription.
        }
        catch (Exception ex)
        {
            Logger.Log("Transcription failed", ex);
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

    // MARK: - Single-instance window signal

    private void StartShowWindowListener()
    {
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _showWindowListener = new Thread(() =>
        {
            while (!_shuttingDown)
            {
                try
                {
                    if (_showWindowEvent.WaitOne(500))
                    {
                        Dispatcher.Invoke(ShowMainWindow);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log("Show-window listener error", ex);
                    break;
                }
            }
        })
        { IsBackground = true, Name = "Orate.ShowWindowListener" };
        _showWindowListener.Start();
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
        menu.Items.Add("Check for Updates…", null, async (_, _) => await CheckForUpdatesInteractiveAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => QuitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon() ?? Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Orate",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var info = GetResourceStream(new Uri("pack://application:,,,/Assets/orate.ico"));
            if (info?.Stream is { } stream)
            {
                using (stream) return new Drawing.Icon(stream);
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load app icon", ex);
        }
        return null;
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
        _shuttingDown = true;
        _showWindowEvent?.Set();   // wake the listener so it can observe _shuttingDown and exit
        _showWindowEvent?.Dispose();
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
