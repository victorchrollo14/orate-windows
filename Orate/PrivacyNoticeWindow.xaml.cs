using System.Diagnostics;
using System.Windows;
using Orate.Services;

namespace Orate;

/// <summary>
/// First-run privacy notice. Shown before the push-to-talk hook starts, so nothing is recorded or
/// sent until the user agrees. Required by the SignPath Foundation code-signing policy, which asks
/// apps that send data over the network to show their privacy policy at install time.
/// </summary>
public partial class PrivacyNoticeWindow : Window
{
    public const string PolicyUrl = "https://github.com/victorchrollo14/orate-windows/blob/main/PRIVACY.md";

    public PrivacyNoticeWindow()
    {
        InitializeComponent();
    }

    public static void OpenPolicy()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PolicyUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to open privacy policy", ex);
        }
    }

    private void OnOpenPolicy(object sender, RoutedEventArgs e) => OpenPolicy();

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDecline(object sender, RoutedEventArgs e) => DialogResult = false;
}
