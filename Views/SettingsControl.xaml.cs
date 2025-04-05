using System.Windows.Controls;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Views;

public partial class SettingsControl : UserControl
{
    public SettingsControl(Settings settings)
    {
        InitializeComponent();
        DataContext = settings;
    }
    
}