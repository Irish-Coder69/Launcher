using System.Windows;

namespace Launcher.App;

public partial class AboutWindow : Window
{
    public bool CheckForUpdatesRequested { get; private set; }

    public AboutWindow(string appVersion)
    {
        InitializeComponent();
        VersionTextBlock.Text = "Version " + appVersion;
    }

    private void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        CheckForUpdatesRequested = true;
        Close();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
