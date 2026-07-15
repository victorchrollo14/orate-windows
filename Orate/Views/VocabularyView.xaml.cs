using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orate.Services;

namespace Orate.Views;

public partial class VocabularyView : UserControl
{
    private readonly List<string> _words = new();

    public VocabularyView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _words.Clear();
            _words.AddRange(SettingsStore.Current.VocabularyWords);
            Refresh();
        };
    }

    private void Refresh()
    {
        Words.ItemsSource = null;
        Words.ItemsSource = _words;

        bool empty = _words.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        WordsSection.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        CountLabel.Text = $"{_words.Count} word{(_words.Count == 1 ? "" : "s")}";
    }

    private void OnNewWordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddWord();
    }

    private void OnAdd(object sender, RoutedEventArgs e) => AddWord();

    private void AddWord()
    {
        var trimmed = NewWord.Text.Trim();
        NewWord.Text = "";
        if (trimmed.Length == 0) return;
        if (_words.Any(w => string.Equals(w, trimmed, StringComparison.OrdinalIgnoreCase))) return;

        _words.Add(trimmed);
        Save();
        Refresh();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not string word) return;
        _words.Remove(word);
        Save();
        Refresh();
    }

    private void Save()
    {
        SettingsStore.Current.VocabularyWords = new List<string>(_words);
        SettingsStore.Save();
    }
}
