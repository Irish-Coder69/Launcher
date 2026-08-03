using System.Diagnostics;
using System.IO;
using System.Windows;
using Launcher.Core.Services;

namespace Launcher.App;

public partial class UpdatesWindow : Window
{
    private readonly string _updateUrl;
    private readonly string _currentVersionText;
    private readonly LauncherUpdateService _updateService = new();
    private LauncherUpdatePackage? _pendingUpdatePackage;
    private string? _downloadedInstallerPath;
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
        DownloadProgressPanel.Visibility = Visibility.Collapsed;
        UpdateStatusTextBlock.Text = "Checking for updates...";
        _pendingUpdatePackage = null;
        _downloadedInstallerPath = null;

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
                + "Download the installer below. You must uninstall the old version before installing the new one.";
            UpdateAvailablePanel.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateStatusTextBlock.Text = "You are up to date.\nCurrent version: " + result.CurrentVersion;
        }

        SetBusy(false);
    }

    private async void DownloadInstallerButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _pendingUpdatePackage is null || string.IsNullOrWhiteSpace(_pendingUpdatePackage.DownloadUrl))
        {
            return;
        }

        try
        {
            SetBusy(true);
            UpdateAvailablePanel.Visibility = Visibility.Collapsed;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 0;
            DownloadProgressTextBlock.Text = "Starting download...";

            var package = _pendingUpdatePackage;
            var fileName = Path.GetFileName(new Uri(package.DownloadUrl!).LocalPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "Launcher-" + package.Version + "-Setup.exe";
            }

            var updateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher", "updates");
            Directory.CreateDirectory(updateDir);
            var installerPath = Path.Combine(updateDir, fileName);

            var progress = new Progress<LauncherUpdateDownloadProgress>(ReportDownloadProgress);
            await _updateService.DownloadFileAsync(package.DownloadUrl!, installerPath, progress);

            if (!LauncherUpdateService.VerifyChecksum(installerPath, package.Checksum))
            {
                UpdateStatusTextBlock.Text = "Downloaded installer failed checksum verification. Download again.";
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                SetBusy(false);
                return;
            }

            _downloadedInstallerPath = installerPath;

            var uninstallPrompt = MessageBox.Show(
                this,
                "Installer downloaded successfully.\n\nBefore installing the new version, uninstall the current Launcher from Windows Apps (Installed Apps).\n\nClick Yes only after uninstall is complete to launch the installer.",
                "Launcher Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (uninstallPrompt != MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + installerPath + "\"",
                    UseShellExecute = true
                });

                UpdateStatusTextBlock.Text = "Installer downloaded. Uninstall the old version first, then run the installer from your Downloads cache.";
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                SetBusy(false);
                return;
            }

            var launchPrompt = MessageBox.Show(
                this,
                "Launch the installer now?",
                "Launcher Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (launchPrompt == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                });

                UpdateStatusTextBlock.Text = "Installer launched. Follow the installer to complete installation.";
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + installerPath + "\"",
                    UseShellExecute = true
                });

                UpdateStatusTextBlock.Text = "Installer downloaded. Launch it when ready after uninstalling old Launcher.";
            }

            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            UpdateAvailablePanel.Visibility = Visibility.Visible;
            SetBusy(false);
        }
        catch (Exception ex)
        {
            UpdateStatusTextBlock.Text = "Installer download failed.\n" + ex.Message;
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
        DownloadInstallerButton.IsEnabled = !isBusy;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
