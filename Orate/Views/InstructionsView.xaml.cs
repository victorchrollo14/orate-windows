using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Orate.Services;

namespace Orate.Views;

public partial class InstructionsView : UserControl
{
    private static readonly string[] ExampleTexts =
    {
        "I'm a doctor. Use proper medical terminology (e.g. \"myocardial infarction\" instead of \"heart attack\").",
        "I write code. Format technical terms in lowercase (e.g. \"kubernetes\", \"nginx\"). Spell out variable-style names as spoken.",
        "I speak with filler words. Remove \"um\", \"uh\", \"like\", and \"you know\" from my speech.",
        "I dictate in Spanish but want transcriptions in English.",
        "Always use Oxford commas and American English spelling.",
    };

    public InstructionsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Editor.Text = SettingsStore.Current.CustomInstructions;
            RefreshClear();
            BuildExamples();
        };
        Editor.TextChanged += (_, _) => RefreshClear();
    }

    private void RefreshClear() =>
        ClearButton.Visibility = string.IsNullOrEmpty(Editor.Text) ? Visibility.Collapsed : Visibility.Visible;

    private void BuildExamples()
    {
        Examples.Children.Clear();
        foreach (var text in ExampleTexts)
        {
            var border = new Border { Style = (Style)Resources["ExampleRow"], Tag = text };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = "", // Segoe MDL2 lightbulb
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A)),
                Margin = new Thickness(0, 1, 10, 0),
                VerticalAlignment = VerticalAlignment.Top,
            });
            panel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
            });
            border.Child = panel;
            border.MouseLeftButtonUp += OnExampleClick;
            Examples.Children.Add(border);
        }
    }

    private void OnExampleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string text }) Editor.Text = text;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.CustomInstructions = Editor.Text;
        SettingsStore.Save();

        SavedLabel.Visibility = Visibility.Visible;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { SavedLabel.Visibility = Visibility.Collapsed; timer.Stop(); };
        timer.Start();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        Editor.Text = "";
        SettingsStore.Current.CustomInstructions = "";
        SettingsStore.Save();
    }
}
