using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Orate.Interop;

namespace Orate.Overlay;

/// <summary>
/// Floating click-through pill at bottom-center of the screen. Port of macOS OverlayPanel:
/// idle (tiny pill) → listening (animated waveform driven by mic level) → transcribing
/// (pulsing dots) → error (red "Error", auto-dismisses).
/// </summary>
public partial class OverlayWindow : Window
{
    private const double IdlePillHeight = 10;
    private const double ActivePillHeight = 34;
    private const int WaveformBarCount = 11;

    private static readonly Brush WhiteBrush = Freeze(new SolidColorBrush(Colors.White));
    private static readonly Brush DarkPill = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));   // 0.5 black
    private static readonly Brush DarkPillActive = Freeze(new SolidColorBrush(Color.FromArgb(0xBF, 0, 0, 0))); // 0.75 black
    private static readonly Brush ErrorPill = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0xD9, 0x26, 0x26)));

    private readonly List<Rectangle> _bars = new();
    private readonly List<double> _smoothed = new();
    private double _phase;
    private double _currentLevel;

    private DispatcherTimer? _errorTimer;

    public bool IsListening { get; private set; }
    public bool IsTranscribing { get; private set; }

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Make the window click-through, non-activating, and absent from the taskbar/alt-tab.
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));

        ShowIdle();
    }

    public void ShowOverlay()
    {
        Show();
        ShowIdle();
    }

    // MARK: - State transitions

    public void SetListening(bool listening)
    {
        if (listening == IsListening) return;
        IsListening = listening;
        if (listening) ShowWaveform();
        else if (!IsTranscribing) ShowIdle();
    }

    public void SetTranscribing(bool transcribing)
    {
        if (transcribing == IsTranscribing) return;
        IsTranscribing = transcribing;
        if (transcribing)
        {
            IsListening = false; // came straight from the waveform — avoid an idle flash
            ShowLoading();
        }
        else ShowIdle();
    }

    public void ShowError()
    {
        _errorTimer?.Stop();
        IsListening = false;
        IsTranscribing = false;
        ShowErrorState();

        _errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _errorTimer.Tick += (_, _) =>
        {
            _errorTimer?.Stop();
            if (!IsListening && !IsTranscribing) ShowIdle();
        };
        _errorTimer.Start();
    }

    /// <summary>
    /// Latches the latest mic level. The actual bar animation runs on a continuous render loop
    /// (<see cref="OnWaveformFrame"/>) so the waveform is alive the instant we start listening,
    /// even before NAudio delivers its first audio buffer (~0.5–1s) — no dead/static pill.
    /// </summary>
    public void UpdateLevel(double level)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateLevel(level));
            return;
        }
        _currentLevel = Math.Max(_currentLevel, level); // peak-hold; the frame loop decays it
    }

    private void OnWaveformFrame(object? sender, EventArgs e)
    {
        if (_bars.Count == 0) return;

        _phase += 0.15;
        double boosted = Math.Pow(_currentLevel, 0.4);
        _currentLevel *= 0.88; // decay toward the idle baseline when no fresh input arrives

        const double minH = 3;
        double maxH = ActivePillHeight - 6;
        const double smoothing = 0.25;
        const double baseline = 0.10; // gentle motion so the pill never looks frozen
        double h = Height;
        double center = (_bars.Count - 1) / 2.0;

        for (int i = 0; i < _bars.Count; i++)
        {
            double distFromCenter = Math.Abs(i - center) / center;          // 0 center … 1 edges
            double sine = Math.Sin(_phase + i * 0.8);
            double variation = 0.7 + 0.3 * sine;                            // 0.7 … 1.0
            double shape = 1.0 - distFromCenter * 0.4;                      // taller center

            double level = Math.Max(boosted, baseline * (0.6 + 0.4 * sine));
            double target = Math.Min(level * variation * shape, 1.0);
            double targetH = minH + (maxH - minH) * target;

            double prev = _smoothed[i];
            double sm = prev + (targetH - prev) * smoothing;
            _smoothed[i] = sm;

            _bars[i].Height = sm;
            Canvas.SetTop(_bars[i], (h - sm) / 2);
        }
    }

    // MARK: - States

    private void ShowIdle()
    {
        ClearContent();
        Resize(28, IdlePillHeight, DarkPill);
    }

    private void ShowWaveform()
    {
        ClearContent();
        _phase = 0;
        _currentLevel = 0;

        const double barWidth = 2.5;
        const double barGap = 2;
        const double minH = 3;
        double h = ActivePillHeight;
        double barsWidth = WaveformBarCount * barWidth + (WaveformBarCount - 1) * barGap;
        double pillWidth = barsWidth + 20;

        Resize(pillWidth, h, DarkPillActive);

        double startX = (pillWidth - barsWidth) / 2;
        for (int i = 0; i < WaveformBarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = barWidth,
                Height = minH,
                RadiusX = barWidth / 2,
                RadiusY = barWidth / 2,
                Fill = WhiteBrush,
            };
            Canvas.SetLeft(bar, startX + i * (barWidth + barGap));
            Canvas.SetTop(bar, (h - minH) / 2);
            PillCanvas.Children.Add(bar);
            _bars.Add(bar);
            _smoothed.Add(minH);
        }

        CompositionTarget.Rendering += OnWaveformFrame; // continuous animation while listening
    }

    private void ShowLoading()
    {
        ClearContent();

        const int dotCount = 3;
        const double dotSize = 4;
        const double dotGap = 6;
        double h = ActivePillHeight;
        double dotsWidth = dotCount * dotSize + (dotCount - 1) * dotGap;
        double pillWidth = dotsWidth + 28;

        Resize(pillWidth, h, DarkPillActive);

        double startX = (pillWidth - dotsWidth) / 2;
        for (int i = 0; i < dotCount; i++)
        {
            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Fill = WhiteBrush,
                Opacity = 0.3,
            };
            Canvas.SetLeft(dot, startX + i * (dotSize + dotGap));
            Canvas.SetTop(dot, (h - dotSize) / 2);
            PillCanvas.Children.Add(dot);

            var anim = new DoubleAnimation(0.3, 1.0, new Duration(TimeSpan.FromSeconds(0.5)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(i * 0.2),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            dot.BeginAnimation(OpacityProperty, anim);
        }
    }

    private void ShowErrorState()
    {
        ClearContent();
        double h = ActivePillHeight;

        var label = new TextBlock
        {
            Text = "Error",
            Foreground = WhiteBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double pillWidth = Math.Ceiling(label.DesiredSize.Width) + 24;

        Resize(pillWidth, h, ErrorPill);

        Canvas.SetLeft(label, (pillWidth - label.DesiredSize.Width) / 2);
        Canvas.SetTop(label, (h - label.DesiredSize.Height) / 2);
        PillCanvas.Children.Add(label);
    }

    // MARK: - Helpers

    private void ClearContent()
    {
        CompositionTarget.Rendering -= OnWaveformFrame; // safe even if not subscribed
        PillCanvas.Children.Clear();
        _bars.Clear();
        _smoothed.Clear();
        _currentLevel = 0;
    }

    private void Resize(double width, double height, Brush background)
    {
        Width = width;
        Height = height;
        Pill.CornerRadius = new CornerRadius(height / 2);
        Pill.Background = background;
        Reposition();
    }

    private void Reposition()
    {
        var work = SystemParameters.WorkArea; // excludes the taskbar
        Left = work.Left + (work.Width - Width) / 2;
        Top = work.Bottom - Height - 8;
    }

    private static Brush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
