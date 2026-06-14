using System.Windows;
using System.Windows.Controls;
using Orate.Services;

namespace Orate.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadFromSettings();
    }

    private static string CredentialKey(AIProvider p) => p switch
    {
        AIProvider.OrateCloud => "orateCloudAPIKey",
        AIProvider.GoogleAI => "geminiAPIKey",
        AIProvider.VertexAI => "vertexAPIKey",
        _ => "orateCloudAPIKey",
    };

    private AIProvider SelectedProvider => (AIProvider)Math.Max(0, ProviderCombo.SelectedIndex);

    private void LoadFromSettings()
    {
        _loading = true;
        var s = SettingsStore.Current;

        ProviderCombo.SelectedIndex = (int)s.Provider;
        VertexProject.Text = s.VertexProjectId ?? "";
        VertexRegion.Text = s.VertexRegion;
        CustomInstructions.Text = s.CustomInstructions;
        VertexPanel.Visibility = s.Provider == AIProvider.VertexAI ? Visibility.Visible : Visibility.Collapsed;

        LoadKeyForProvider();
        _loading = false;
    }

    private void LoadKeyForProvider()
    {
        var key = CredentialStore.Read(CredentialKey(SelectedProvider)) ?? "";
        KeyBox.Password = key;
        KeyBoxPlain.Text = key;
        KeyStatus.Text = string.IsNullOrEmpty(key) ? "No key saved" : "Key saved";
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        var provider = SelectedProvider;
        SettingsStore.Current.Provider = provider;
        SettingsStore.Save();

        VertexPanel.Visibility = provider == AIProvider.VertexAI ? Visibility.Visible : Visibility.Collapsed;
        LoadKeyForProvider();
    }

    private void OnToggleShowKey(object sender, RoutedEventArgs e)
    {
        if (ShowKey.IsChecked == true)
        {
            KeyBoxPlain.Text = KeyBox.Password;
            KeyBoxPlain.Visibility = Visibility.Visible;
            KeyBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            KeyBox.Password = KeyBoxPlain.Text;
            KeyBox.Visibility = Visibility.Visible;
            KeyBoxPlain.Visibility = Visibility.Collapsed;
        }
    }

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        var key = ShowKey.IsChecked == true ? KeyBoxPlain.Text : KeyBox.Password;
        key = key.Trim();
        if (string.IsNullOrEmpty(key))
        {
            CredentialStore.Delete(CredentialKey(SelectedProvider));
            KeyStatus.Text = "Key cleared";
            return;
        }
        CredentialStore.Save(CredentialKey(SelectedProvider), key);
        KeyStatus.Text = "Key saved";
    }

    private void OnVertexChanged(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.VertexProjectId = VertexProject.Text.Trim();
        SettingsStore.Current.VertexRegion = string.IsNullOrWhiteSpace(VertexRegion.Text) ? "us-central1" : VertexRegion.Text.Trim();
        SettingsStore.Save();
    }

    private void OnInstructionsChanged(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.CustomInstructions = CustomInstructions.Text;
        SettingsStore.Save();
    }
}
