using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Orate.Views;

namespace Orate;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Nav.SelectedIndex = 0; // Home
    }

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        Detail.Content = Nav.SelectedIndex switch
        {
            1 => new InstructionsView(),
            2 => new VocabularyView(),
            3 => new SettingsView(),
            _ => new HomeView(),
        };
    }

    /// <summary>Set by the app when it is genuinely shutting down, to allow a real close.</summary>
    public bool AllowClose { get; set; }

    /// <summary>Closing the window hides it to the tray; the app keeps running in the background.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        Hide();
    }

    public void NavigateToSettings()
    {
        Nav.SelectedIndex = 3;
    }
}
