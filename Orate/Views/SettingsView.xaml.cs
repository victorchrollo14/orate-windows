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

        _editingKey = false;
        RefreshKeyUi();
        _loading = false;
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

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
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

    private void OnInstructionsChanged(object sender, RoutedEventArgs e)
    {
        SettingsStore.Current.CustomInstructions = CustomInstructions.Text;
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
