using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orate.Services;

namespace Orate.Views;

public partial class HistoryView : UserControl
{
    /// <summary>Display wrapper around a saved recording.</summary>
    public sealed class HistoryItem
    {
        public required RecordingMetadata Meta { get; init; }
        public string Transcript => Meta.Transcript;
        public string Info =>
            $"{Meta.Timestamp:MMM d, yyyy · h:mm tt}   ·   {Meta.Model}   ·   {Meta.LatencyMs} ms   ·   {Meta.AudioSizeBytes / 1024} KB";
    }

    private readonly MediaPlayer _player = new();

    public HistoryView()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        var items = RecordingStore.LoadAll().Select(m => new HistoryItem { Meta = m }).ToList();
        List.ItemsSource = items;

        bool empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ListScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        CountLabel.Text = empty ? "" : $"{items.Count} transcription{(items.Count == 1 ? "" : "s")}";
    }

    private static HistoryItem? ItemFrom(object sender) =>
        (sender as FrameworkElement)?.DataContext as HistoryItem;

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
            Logger.Log("History: playback failed", ex);
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        try { Clipboard.SetText(item.Transcript); }
        catch (Exception ex) { Logger.Log("History: copy failed", ex); }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is not { } item) return;
        RecordingStore.Delete(item.Meta);
        Reload();
    }

    private void OnClearAll(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Delete all saved transcriptions? This cannot be undone.",
            "Clear History", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        RecordingStore.Clear(null);
        Reload();
    }
}
