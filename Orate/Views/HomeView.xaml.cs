using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orate.Services;

namespace Orate.Views;

public partial class HomeView : UserControl
{
    /// <summary>Display wrapper around a saved recording (mirrors macOS HomeView row).</summary>
    public sealed class HomeItem
    {
        public required RecordingMetadata Meta { get; init; }
        public string Transcript => Meta.Transcript;

        public string Info
        {
            get
            {
                var time = Meta.Timestamp.ToString("MMM d, yyyy 'at' h:mm tt");
                var n = Meta.Transcript.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                return $"{time}   ·   {Meta.LatencyMs}ms   ·   {n} word{(n == 1 ? "" : "s")}";
            }
        }
    }

    private readonly MediaPlayer _player = new();

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            HotkeyInline.Text = $" {VirtualKeys.DisplayName(SettingsStore.Current.PushToTalkVk)} ";
            Reload();
        };
    }

    private void Reload()
    {
        var items = RecordingStore.LoadAll().Select(m => new HomeItem { Meta = m }).ToList();
        List.ItemsSource = items;

        bool empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListSection.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyHint.Text = $"Hold {VirtualKeys.DisplayName(SettingsStore.Current.PushToTalkVk)} to start your first recording.";
    }

    private static HomeItem? ItemFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as HomeItem;

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        try
        {
            _player.Stop();
            _player.Open(new Uri(item.Meta.AudioPath));
            _player.Play();
        }
        catch (Exception ex)
        {
            Logger.Log("Home: playback failed", ex);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        try { Clipboard.SetText(item.Transcript); }
        catch (Exception ex) { Logger.Log("Home: copy failed", ex); }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        menu.Items.Add(ClearMenuItem("Older than 7 days", 7));
        menu.Items.Add(ClearMenuItem("Older than 14 days", 14));
        menu.Items.Add(ClearMenuItem("Older than 30 days", 30));
        menu.Items.Add(new Separator());
        menu.Items.Add(ClearMenuItem("All Recordings", null));

        if (sender is UIElement el)
        {
            menu.PlacementTarget = el;
            menu.IsOpen = true;
        }
    }

    private MenuItem ClearMenuItem(string header, int? olderThanDays)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => ClearRecordings(olderThanDays);
        return mi;
    }

    private void ClearRecordings(int? olderThanDays)
    {
        int before = RecordingStore.LoadAll().Count;
        RecordingStore.Clear(olderThanDays);
        int deleted = before - RecordingStore.LoadAll().Count;
        Reload();

        ClearedLabel.Text = deleted > 0
            ? $"✓ {deleted} recording{(deleted == 1 ? "" : "s")} cleared"
            : "No recordings to clear";
        ClearedLabel.Visibility = Visibility.Visible;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { ClearedLabel.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }
}
