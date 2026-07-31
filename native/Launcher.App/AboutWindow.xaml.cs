using System.Diagnostics;
using System.IO;
using System.Windows;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class AboutWindow : Window
{
    private readonly string _launcherRoot;
    private readonly string _updateUrl;
    private readonly string _currentVersionText;
    private readonly LauncherUpdateService _updateService = new();
    private LauncherUpdatePackage? _pendingUpdatePackage;
    private bool _isBusy;

    public AboutWindow(string appVersion, string launcherRoot, string updateUrl)
    {
        InitializeComponent();

        _launcherRoot = launcherRoot;
        _updateUrl = updateUrl;
        _currentVersionText = appVersion;

        VersionTextBlock.Text = "Version " + appVersion;
        UpdatesCurrentVersionTextBlock.Text = "Current version " + appVersion;
    }

    private async void CheckUpdatesButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
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

            UpdateStatusTextBlock.Text = "Update available: " + result.LatestPackage.Version + " (current " + result.CurrentVersion + ")";
            UpdateAvailablePanel.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusTextBlock.Text = "You are up to date.\nCurrent version: " + result.CurrentVersion;
        }

        SetBusy(false);
    }

    private async void DownloadInstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _pendingUpdatePackage is null || string.IsNullOrWhiteSpace(_pendingUpdatePackage.DownloadUrl))
        {
            return;
        }

        var package = _pendingUpdatePackage;
        SetBusy(true);
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        DownloadProgressTextBlock.Text = "Starting download...";

        try
        {
            var fileName = Path.GetFileName(new Uri(package.DownloadUrl!).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "Launcher-" + package.Version + "-Setup.exe";
            }

            var updateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher", "updates");
            var installerPath = Path.Combine(updateDir, fileName);

            var progress = new Progress<LauncherUpdateDownloadProgress>(ReportDownloadProgress);
            await _updateService.DownloadFileAsync(package.DownloadUrl!, installerPath, progress);

            if (!LauncherUpdateService.VerifyChecksum(installerPath, package.Checksum))
            {
                UpdateStatusTextBlock.Text = "Downloaded installer failed checksum verification. Update was not installed.";
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                SetBusy(false);
                return;
            }

            DownloadProgressTextBlock.Text = "Download complete. Starting installation...";

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = $"/S /D=\"{_launcherRoot}\"",
                UseShellExecute = false,
                WorkingDirectory = updateDir
            };

            Process.Start(startInfo);

            // The installer needs to replace this running executable, so close immediately without prompting.
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = "Update download failed.\n" + ex.Message;
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            UpdateAvailablePanel.Visibility = Visibility.Visible;
            SetBusy(false);
        }
    }

    private void ReportDownloadProgress(LauncherUpdateDownloadProgress progress)
    {
        var receivedMb = progress.BytesReceived / 1024d / 1024d;

        if (progress.TotalBytes is > 0)
        {
            var totalMb = progress.TotalBytes.Value / 1024d / 1024d;
            var percent = progress.PercentComplete ?? 0;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = percent;
            DownloadProgressTextBlock.Text = $"{receivedMb:0.0} MB / {totalMb:0.0} MB ({percent:0}%)";
        }
        else
        {
            DownloadProgressBar.IsIndeterminate = true;
            DownloadProgressTextBlock.Text = $"{receivedMb:0.0} MB downloaded";
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        CheckUpdatesButton.IsEnabled = !isBusy;
        DownloadInstallButton.IsEnabled = !isBusy;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
