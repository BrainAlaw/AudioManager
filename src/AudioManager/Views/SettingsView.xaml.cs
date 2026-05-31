using System.Diagnostics;
using WpfUserControl = System.Windows.Controls.UserControl;
using System.Windows.Navigation;

namespace AudioManager.Views;

public partial class SettingsView : WpfUserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void RepositoryLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
