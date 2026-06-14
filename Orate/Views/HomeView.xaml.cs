using System.Windows.Controls;
using Orate.Services;

namespace Orate.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            var s = SettingsStore.Current;
            var key = VirtualKeys.DisplayName(s.PushToTalkVk);
            HotkeyInline.Text = $" {key} ";
            HotkeyLabel.Text = key;
            ProviderLabel.Text = s.Provider.DisplayName();
        };
    }
}
