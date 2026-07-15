using System.Windows;
using System.Windows.Controls;
using Orate.Services;

namespace Orate.Views;

public partial class SettingsView : UserControl
{
    private bool _loading;
    private bool _editingKey; // true while the user is entering/replacing a key

    public SettingsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadFromSettings();
            RefreshAbout();
            App.Instance.UpdateStateChanged += OnUpdateStateChanged;
        };
        Unloaded += (_, _) => App.Instance.UpdateStateChanged -= OnUpdateStateChanged;
    }

    private void OnUpdateStateChanged() => Dispatcher.Invoke(RefreshAbout);

    private void RefreshAbout()
    {
        VersionLabel.Text = $"Orate {App.Instance.CurrentVersionDisplay}";
        UpdateButton.Visibility = App.Instance.IsUpdateReady ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRestartToUpdate(object sender, RoutedEventArgs e) => App.Instance.RestartToApplyUpdate();

    private static string CredentialKey(AIProvider p) => p switch
    {
        AIProvider.OrateCloud => "orateCloudAPIKey",
        AIProvider.GoogleAI => "geminiAPIKey",
        AIProvider.VertexAI => "vertexAPIKey",
        _ => "orateCloudAPIKey",
    };

    private AIProvider SelectedProvider =>
        ProviderGoogle.IsChecked == true ? AIProvider.GoogleAI :
        ProviderVertex.IsChecked == true ? AIProvider.VertexAI :
        AIProvider.OrateCloud;

    private void LoadFromSettings()
    {
        _loading = true;
        var s = SettingsStore.Current;

        ProviderOrate.IsChecked = s.Provider == AIProvider.OrateCloud;
        ProviderGoogle.IsChecked = s.Provider == AIProvider.GoogleAI;
        ProviderVertex.IsChecked = s.Provider == AIProvider.VertexAI;

        VertexProject.Text = s.VertexProjectId ?? "";
        VertexRegion.Text = s.VertexRegion;
        VertexPanel.Visibility = s.Provider == AIProvider.VertexAI ? Visibility.Visible : Visibility.Collapsed;

        _editingKey = false;
        UpdateApiKeyLabels(s.Provider);
        RefreshKeyUi();
        _loading = false;
    }

    private void UpdateApiKeyLabels(AIProvider provider)
    {
        ApiKeyHeader.Text = $"{provider.DisplayName()} API Key";
        ApiKeyHint.Text = provider switch
        {
            AIProvider.OrateCloud => "Orate Cloud is the easiest way to get started. Enter your API key below to start transcribing.",
            AIProvider.GoogleAI => "Orate uses Google's Gemini API to transcribe your audio. You'll need an API key from Google AI Studio.",
            AIProvider.VertexAI => "Use Vertex AI through your Google Cloud project. You'll need an API key from the GCP console with Vertex AI access.",
            _ => "",
        };
    }

    /// <summary>Shows the "saved" chip when a key exists, otherwise the entry field. Never
    /// displays the stored key itself.</summary>
    private void RefreshKeyUi()
    {
        bool hasKey = !string.IsNullOrEmpty(CredentialStore.Read(CredentialKey(SelectedProvider)));

        if (hasKey && !_editingKey)
        {
            KeySavedPanel.Visibility = Visibility.Visible;
            KeyEditPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            KeySavedPanel.Visibility = Visibility.Collapsed;
            KeyEditPanel.Visibility = Visibility.Visible;
            CancelKeyButton.Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnProviderChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var provider = SelectedProvider;
        SettingsStore.Current.Provider = provider;
        SettingsStore.Save();

        VertexPanel.Visibility = provider == AIProvider.VertexAI ? Visibility.Visible : Visibility.Collapsed;
        _editingKey = false;
        KeyBox.Password = "";
        KeyBoxPlain.Text = "";
        KeyStatus.Text = "";
        UpdateApiKeyLabels(provider);
        RefreshKeyUi();
    }

    private void OnChangeKey(object sender, RoutedEventArgs e)
    {
        _editingKey = true;
        KeyBox.Password = "";
        KeyBoxPlain.Text = "";
        KeyStatus.Text = "";
        RefreshKeyUi();
        KeyBox.Focus();
    }

    private void OnCancelKey(object sender, RoutedEventArgs e)
    {
        _editingKey = false;
        RefreshKeyUi();
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
            KeyStatus.Text = "Enter a key to save";
            _editingKey = false;
            RefreshKeyUi();
            return;
        }
        CredentialStore.Save(CredentialKey(SelectedProvider), key);
        KeyBox.Password = "";
        KeyBoxPlain.Text = "";
        ShowKey.IsChecked = false;
        _editingKey = false;
        RefreshKeyUi();
    }

    private void OnVertexChanged(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.VertexProjectId = VertexProject.Text.Trim();
        SettingsStore.Current.VertexRegion = string.IsNullOrWhiteSpace(VertexRegion.Text) ? "us-central1" : VertexRegion.Text.Trim();
        SettingsStore.Save();
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.IO.File.Exists(Logger.Path_))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{Logger.Path_}\"") { UseShellExecute = false });
            }
            else
            {
                KeyStatus.Text = "No log file yet";
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to open log", ex);
        }
    }

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(Logger.Path_)!;
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to open log folder", ex);
        }
    }
}
