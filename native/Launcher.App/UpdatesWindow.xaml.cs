using System.Diagnostics;
using System.Windows;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class UpdatesWindow : Window
{
    private readonly string _updateUrl;
    private readonly string _currentVersionText;
    private readonly LauncherUpdateService _updateService = new();
    private LauncherUpdatePackage? _pendingUpdatePackage;
    private bool _isBusy;

    public UpdatesWindow(string appVersion, string updateUrl)
    {
        InitializeComponent();

        _updateUrl = updateUrl;
        _currentVersionText = appVersion;

        UpdatesCurrentVersionTextBlock.Text = "Current version " + appVersion;

        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        UpdateStatusTextBlock.Text = "Checking for updates...";
        _pendingUpdatePackage = null;

        var result = await _updateService.CheckForUpdateAsync(_updateUrl, _currentVersionText);

        if (!result.Success)
        {
            UpdateStatusTextBlock.Text = "Update check failed.\n" + result.ErrorMessage;
            SetBusy(false);
            return;
        }

        if (result.IsUpdateAvailable && result.LatestPackage is not null)
        {
            _pendingUpdatePackage = result.LatestPackage;

            if (string.IsNullOrWhiteSpace(result.LatestPackage.DownloadUrl))
            {
                UpdateStatusTextBlock.Text = "Update available: " + result.LatestPackage.Version +
                    " (current " + result.CurrentVersion + ")\nNo installer download URL was provided by the feed.";
                SetBusy(false);
                return;
            }

            UpdateStatusTextBlock.Text = "Update available: " + result.LatestPackage.Version + " (current " + result.CurrentVersion + ")\n"
                + "This app does not self-update. Use the button below to download the installer, uninstall the old version from Windows, then run the new installer.";
            UpdateAvailablePanel.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusTextBlock.Text = "You are up to date.\nCurrent version: " + result.CurrentVersion;
        }

        SetBusy(false);
    }

    private void OpenDownloadPageButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _pendingUpdatePackage is null || string.IsNullOrWhiteSpace(_pendingUpdatePackage.DownloadUrl))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _pendingUpdatePackage.DownloadUrl!,
                UseShellExecute = true
            };

            Process.Start(startInfo);

            UpdateStatusTextBlock.Text = "Installer page opened. Uninstall the current Launcher from Windows Apps, then run the downloaded installer.";
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = "Could not open installer download page.\n" + ex.Message;
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        CheckUpdatesButton.IsEnabled = !isBusy;
        OpenDownloadPageButton.IsEnabled = !isBusy;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
