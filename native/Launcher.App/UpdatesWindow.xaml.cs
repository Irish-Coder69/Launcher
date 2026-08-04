using System.Diagnostics;
using System.IO;
using System.Text;
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

            var startGuidedUpdate = MessageBox.Show(
                this,
                "Installer downloaded successfully.\n\nStart guided update now?\n\nThis will close Launcher, open old-version uninstall, then open the new installer after you confirm uninstall is finished.",
                "Launcher Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (startGuidedUpdate != MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + installerPath + "\"",
                    UseShellExecute = true
                });

                UpdateStatusTextBlock.Text = "Installer downloaded. You can run it manually when ready.";
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                UpdateAvailablePanel.Visibility = Visibility.Visible;
                SetBusy(false);
                return;
            }

            StartGuidedUninstallAndInstall(installerPath);
            UpdateStatusTextBlock.Text = "Starting guided update: closing Launcher, then launching uninstall and installer sequence.";
            Application.Current.Shutdown();
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

    private static void StartGuidedUninstallAndInstall(string installerPath)
    {
        var launcherPid = Environment.ProcessId;
        var escapedInstallerPath = installerPath.Replace("'", "''");
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
$launcherPid = {launcherPid}

try {{
    Wait-Process -Id $launcherPid -ErrorAction SilentlyContinue
}} catch {{}}

$uninstallCmd = $null
try {{
    $uninstallCmd = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Launcher' -Name 'UninstallString' -ErrorAction Stop).UninstallString
}} catch {{}}

if ([string]::IsNullOrWhiteSpace($uninstallCmd)) {{
    $fallbackUninstallPath = Join-Path $env:LOCALAPPDATA 'Programs\Launcher\Uninstall.exe'
    if (Test-Path $fallbackUninstallPath) {{
        $quote = [char]34
        $uninstallCmd = $quote + $fallbackUninstallPath + $quote
    }}
}}

if (-not [string]::IsNullOrWhiteSpace($uninstallCmd)) {{
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $uninstallCmd -Wait
}} else {{
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Could not find Launcher uninstaller automatically. Please uninstall Launcher manually from Installed Apps, then click OK to continue.', 'Launcher Update') | Out-Null
}}

Add-Type -AssemblyName PresentationFramework
$confirm = [System.Windows.MessageBox]::Show('Click OK after uninstall has fully finished to start installing the new Launcher version.', 'Launcher Update', 'OKCancel', 'Question')
if ($confirm -ne 'OK') {{
    exit
}}

if (Test-Path '{escapedInstallerPath}') {{
    Start-Process -FilePath '{escapedInstallerPath}' -Wait
}} else {{
    [System.Windows.MessageBox]::Show('Downloaded installer was not found: {escapedInstallerPath}', 'Launcher Update') | Out-Null
}}
";

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
