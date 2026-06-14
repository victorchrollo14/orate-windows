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
            HotkeyLabel.Text = VirtualKeys.DisplayName(s.PushToTalkVk);
            ProviderLabel.Text = $"Provider: {s.Provider.DisplayName()}";
        };
    }
}
