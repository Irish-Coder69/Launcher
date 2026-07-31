using System.Windows;

namespace Launcher.App;

public partial class AboutWindow : Window
{
    public AboutWindow(string appVersion)
    {
        InitializeComponent();

        VersionTextBlock.Text = "Version " + appVersion;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
