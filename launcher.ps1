[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification='Interactive launcher intentionally renders colorized host UI.')]
param(
    [string]$ConfigPath = ".\launcher.config.json",
    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$script:LogFile = Join-Path -Path $PSScriptRoot -ChildPath "launcher.last.log"
$script:VersionFile = Join-Path -Path $PSScriptRoot -ChildPath "version.txt"
$script:LauncherVersion = "unknown"
if (Test-Path -Path $script:VersionFile) {
    try {
        $loadedVersion = (Get-Content -Path $script:VersionFile -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($loadedVersion)) {
            $script:LauncherVersion = $loadedVersion
        }
    }
    catch {
        $null = $_
    }
}

# --- Console appearance ---
$Host.UI.RawUI.WindowTitle = if ($DryRun) { "Launcher v$($script:LauncherVersion) (Dry Run)" } else { "Launcher v$($script:LauncherVersion)" }
try {
    $Host.UI.RawUI.BackgroundColor = [System.ConsoleColor]::Black
    $Host.UI.RawUI.ForegroundColor = [System.ConsoleColor]::Gray
    Clear-Host
} catch { $null = $_ }

try {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LauncherWindowState {
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
}
"@ -ErrorAction SilentlyContinue

    $launcherProcess = Get-Process -Id $PID -ErrorAction SilentlyContinue
    if ($launcherProcess -and $launcherProcess.MainWindowHandle -ne 0) {
        [void][LauncherWindowState]::ShowWindowAsync($launcherProcess.MainWindowHandle, 3)
    }
} catch { $null = $_ }

try {
    $currentWindowSize = $Host.UI.RawUI.WindowSize
    $bufferWidth = [Math]::Max($currentWindowSize.Width, 90)
    $bufSize  = New-Object System.Management.Automation.Host.Size($bufferWidth, 2000)
    $Host.UI.RawUI.BufferSize = $bufSize
} catch { $null = $_ }
$script:UIWidth = 88

function Write-LauncherLog {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR")]
        [string]$Level = "INFO"
    )

    $timestamp = Get-Date -Format "MM-dd-yyyy HH:mm:ss"
    $line = "[$timestamp][$Level] $Message"
    Add-Content -Path $script:LogFile -Value $line

    if ($Message -like "\[DryRun\]*") {
        Write-Host "  $Message" -ForegroundColor DarkCyan
    } elseif ($Level -eq "ERROR") {
        Write-Host "  [ERROR] $Message" -ForegroundColor Red
    } elseif ($Level -eq "WARN") {
        Write-Host "  [!] $Message" -ForegroundColor Yellow
    } else {
        Write-Host "  $Message" -ForegroundColor Gray
    }
}

function Show-LauncherBanner {
    param([switch]$IsDryRun)
    $w = $script:UIWidth
    $label = if ($IsDryRun) { "  LAUNCHER v$($script:LauncherVersion)  -  DRY RUN  " } else { "  LAUNCHER v$($script:LauncherVersion)  " }
    $pad   = [Math]::Max(0, [int](($w - $label.Length) / 2))
    Write-Host ""
    Write-Host (("=" * $w)) -ForegroundColor DarkCyan
    Write-Host (" " * $pad + $label) -ForegroundColor White
    Write-Host (("=" * $w)) -ForegroundColor DarkCyan
    Write-Host ""
}

function Write-StepHeader {
    param([string]$Name)
    Write-Host ""
    Write-Host "  >  $Name" -ForegroundColor White
    Write-Host ("  " + ("-" * ($script:UIWidth - 4))) -ForegroundColor DarkGray
}

function Wait-ForLauncherCloseCommand {
    param(
        [switch]$DryRun
    )

    if ($DryRun) {
        return
    }

    Write-Host ""
    Write-Host "  Launcher is idle. Type CLOSE and press Enter to exit." -ForegroundColor DarkGray

    while ($true) {
        $commandInput = Read-Host "  Command"
        if ([string]::IsNullOrWhiteSpace($commandInput)) {
            continue
        }

        if ($commandInput.Trim().ToUpperInvariant() -eq "CLOSE") {
            break
        }

        Write-Host "  Type CLOSE to exit launcher." -ForegroundColor Yellow
    }
}

function Resolve-StepPath {
    param(
        [string]$Path,
        [string]$ConfigDirectory
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $Path
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($ConfigDirectory, $Path))
}

function Get-LauncherHostPath {
    $pwshPath = Join-Path -Path $PSHOME -ChildPath "pwsh.exe"
    if (Test-Path -Path $pwshPath) {
        return $pwshPath
    }

    $currentProcess = Get-Process -Id $PID -ErrorAction SilentlyContinue
    if ($currentProcess -and -not [string]::IsNullOrWhiteSpace($currentProcess.Path)) {
        return $currentProcess.Path
    }

    throw "Could not resolve the PowerShell host executable path."
}

function Update-DesktopLauncherLink {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [string]$ScriptDirectory
    )

    $desktopPath = [Environment]::GetFolderPath("Desktop")
    if ([string]::IsNullOrWhiteSpace($desktopPath) -or -not (Test-Path -Path $desktopPath)) {
        Write-LauncherLog "Desktop path could not be resolved; skipping shortcut refresh" -Level "WARN"
        return
    }

    $scriptPath = Join-Path -Path $ScriptDirectory -ChildPath "launcher.ps1"
    if (-not (Test-Path -Path $scriptPath)) {
        Write-LauncherLog "Launcher script was not found at $scriptPath; skipping shortcut refresh" -Level "WARN"
        return
    }

    $pwshPath = Get-LauncherHostPath
    $shell = New-Object -ComObject WScript.Shell
    $mainConfigPath = Join-Path -Path $ScriptDirectory -ChildPath "launcher.config.json"

    $shortcutSpecs = @(
        @{
            Name      = "Launcher.lnk"
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -ConfigPath `"$mainConfigPath`""
        }
    )

    $obsoleteShortcutNames = @(
        "Launcher (Dry Run).lnk",
        "Launcher (Ultra Slow).lnk"
    )

    foreach ($spec in $shortcutSpecs) {
        $shortcutPath = Join-Path -Path $desktopPath -ChildPath $spec.Name
        if ($PSCmdlet.ShouldProcess($shortcutPath, "Refresh desktop launcher shortcut")) {
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = $pwshPath
            $shortcut.Arguments = $spec.Arguments
            $shortcut.WorkingDirectory = $ScriptDirectory
            $shortcut.IconLocation = "$pwshPath,0"
            $shortcut.Save()
            Write-LauncherLog "Refreshed desktop shortcut '$($spec.Name)'"
        }
    }

    foreach ($obsoleteName in $obsoleteShortcutNames) {
        $obsoletePath = Join-Path -Path $desktopPath -ChildPath $obsoleteName
        if ((Test-Path -Path $obsoletePath) -and $PSCmdlet.ShouldProcess($obsoletePath, "Remove obsolete desktop launcher shortcut")) {
            Remove-Item -Path $obsoletePath -Force
            Write-LauncherLog "Removed obsolete desktop shortcut '$obsoleteName'"
        }
    }
}

$script:UpdateIntegrationPath = Join-Path -Path $PSScriptRoot -ChildPath "update\Update-Integration.ps1"
if (Test-Path -Path $script:UpdateIntegrationPath) {
    . $script:UpdateIntegrationPath
}

function Get-PreferredEditControl {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string[]]$PreferredNames = @(),
        [string[]]$ExcludedNames = @(),
        [switch]$RequirePreferredName
    )

    if (-not $Window) {
        return $null
    }

    $editCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Edit
    )

    $editControls = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCondition)
    if (-not $editControls -or $editControls.Count -eq 0) {
        return $null
    }

    foreach ($name in $PreferredNames) {
        if ([string]::IsNullOrWhiteSpace($name)) {
            continue
        }

        foreach ($control in $editControls) {
            $controlName = [string]$control.Current.Name
            $automationId = [string]$control.Current.AutomationId

            $isExcluded = $false
            foreach ($excluded in $ExcludedNames) {
                if ([string]::IsNullOrWhiteSpace($excluded)) {
                    continue
                }

                if ((-not [string]::IsNullOrWhiteSpace($controlName) -and $controlName -like "*$excluded*") -or
                    (-not [string]::IsNullOrWhiteSpace($automationId) -and $automationId -like "*$excluded*")) {
                    $isExcluded = $true
                    break
                }
            }

            if ($isExcluded) {
                continue
            }

            if ((-not [string]::IsNullOrWhiteSpace($controlName) -and $controlName -like "*$name*") -or
                (-not [string]::IsNullOrWhiteSpace($automationId) -and $automationId -like "*$name*")) {
                return $control
            }
        }
    }

    if ($RequirePreferredName -and $PreferredNames -and $PreferredNames.Count -gt 0) {
        return $null
    }

    foreach ($control in $editControls) {
        $controlName = [string]$control.Current.Name
        $automationId = [string]$control.Current.AutomationId

        $isExcluded = $false
        foreach ($excluded in $ExcludedNames) {
            if ([string]::IsNullOrWhiteSpace($excluded)) {
                continue
            }

            if ((-not [string]::IsNullOrWhiteSpace($controlName) -and $controlName -like "*$excluded*") -or
                (-not [string]::IsNullOrWhiteSpace($automationId) -and $automationId -like "*$excluded*")) {
                $isExcluded = $true
                break
            }
        }

        if (-not $isExcluded) {
            return $control
        }
    }

    return $null
}

function Test-EditControlMatchesPreferredName {
    param(
        [System.Windows.Automation.AutomationElement]$Control,
        [string[]]$PreferredNames = @(),
        [string[]]$ExcludedNames = @()
    )

    if (-not $Control) {
        return $false
    }

    if ($Control.Current.ControlType -ne [System.Windows.Automation.ControlType]::Edit) {
        return $false
    }

    if (-not $PreferredNames -or $PreferredNames.Count -eq 0) {
        return $true
    }

    $controlName = [string]$Control.Current.Name
    $automationId = [string]$Control.Current.AutomationId

    foreach ($excluded in $ExcludedNames) {
        if ([string]::IsNullOrWhiteSpace($excluded)) {
            continue
        }

        if ((-not [string]::IsNullOrWhiteSpace($controlName) -and $controlName -like "*$excluded*") -or
            (-not [string]::IsNullOrWhiteSpace($automationId) -and $automationId -like "*$excluded*")) {
            return $false
        }
    }

    foreach ($preferred in $PreferredNames) {
        if ([string]::IsNullOrWhiteSpace($preferred)) {
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($controlName) -and $controlName -like "*$preferred*") {
            return $true
        }

        if (-not [string]::IsNullOrWhiteSpace($automationId) -and $automationId -like "*$preferred*") {
            return $true
        }
    }

    # Access forms often expose unnamed edit controls. If it is an Edit and not excluded,
    # treat it as valid when preferred names are unavailable.
    if ([string]::IsNullOrWhiteSpace($controlName) -and [string]::IsNullOrWhiteSpace($automationId)) {
        return $true
    }

    return $false
}

function Test-StepWindowActivation {
    param(
        [object]$Shell,
        [string]$PrimaryWindowTitle,
        [int]$ProcessId,
        [string[]]$FallbackWindowTitles
    )

    if (-not [string]::IsNullOrWhiteSpace($PrimaryWindowTitle) -and $Shell.AppActivate($PrimaryWindowTitle)) {
        return $true
    }

    if ($ProcessId -gt 0 -and $Shell.AppActivate($ProcessId)) {
        return $true
    }

    foreach ($candidate in $FallbackWindowTitles) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and $Shell.AppActivate([string]$candidate)) {
            return $true
        }
    }

    $windowedProcesses = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.MainWindowHandle -ne 0 -and -not [string]::IsNullOrWhiteSpace($_.MainWindowTitle)
    }

    if ($ProcessId -gt 0) {
        $byId = $windowedProcesses | Where-Object { $_.Id -eq $ProcessId } | Select-Object -First 1
        if ($byId -and $Shell.AppActivate($byId.MainWindowTitle)) {
            return $true
        }
    }

    foreach ($candidate in $FallbackWindowTitles) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $byTitleContains = $windowedProcesses | Where-Object {
            $_.MainWindowTitle -like "*$candidate*"
        } | Select-Object -First 1

        if ($byTitleContains -and $Shell.AppActivate($byTitleContains.MainWindowTitle)) {
            return $true
        }
    }

    return $false
}

function Test-AnyWindowTitleMatch {
    param(
        [string[]]$CandidateTitles,
        [int]$ProcessId = 0
    )

    if (-not $CandidateTitles -or $CandidateTitles.Count -eq 0) {
        return $false
    }

    $windowedProcesses = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.MainWindowHandle -ne 0 -and -not [string]::IsNullOrWhiteSpace($_.MainWindowTitle)
    }

    if ($ProcessId -gt 0) {
        $windowedProcesses = $windowedProcesses | Where-Object { $_.Id -eq $ProcessId }
    }

    foreach ($candidate in $CandidateTitles) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        foreach ($proc in $windowedProcesses) {
            $title = [string]$proc.MainWindowTitle
            if ([string]::IsNullOrWhiteSpace($title)) {
                continue
            }

            if ($title -eq $candidate -or $title -like "*$candidate*") {
                return $true
            }
        }
    }

    return $false
}

function Test-WindowContainsCandidateControl {
    param(
        [string[]]$WindowTitles,
        [string[]]$CandidateNames,
        [int]$ProcessId = 0
    )

    if (-not $CandidateNames -or $CandidateNames.Count -eq 0) {
        return $true
    }

    if (-not $WindowTitles -or $WindowTitles.Count -eq 0) {
        return $false
    }

    try {
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes

        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windowControlTypeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window
        )

        $allWindows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $windowControlTypeCondition)

        $targetWindow = $null
        foreach ($title in $WindowTitles) {
            if ([string]::IsNullOrWhiteSpace($title)) {
                continue
            }

            foreach ($w in $allWindows) {
                $windowName = [string]$w.Current.Name
                if ([string]::IsNullOrWhiteSpace($windowName)) {
                    continue
                }

                if ($windowName -eq $title -or $windowName -like "*$title*") {
                    if ($ProcessId -gt 0 -and $w.Current.ProcessId -ne $ProcessId) {
                        continue
                    }

                    $targetWindow = $w
                    break
                }
            }

            if ($targetWindow) {
                break
            }
        }

        if (-not $targetWindow -and $ProcessId -gt 0) {
            foreach ($w in $allWindows) {
                if ($w.Current.ProcessId -eq $ProcessId) {
                    $targetWindow = $w
                    break
                }
            }
        }

        if (-not $targetWindow) {
            return $false
        }

        $control = Find-ControlByCandidateName -Window $targetWindow -CandidateNames $CandidateNames
        return [bool]$control
    }
    catch {
        return $false
    }
}

function Confirm-LoginCompletion {
    param(
        [object]$Step,
        [System.Diagnostics.Process]$Process,
        [switch]$DryRun
    )

    $validationEnabled = $false
    if ($Step.PSObject.Properties.Name -contains "loginSuccessValidationEnabled") {
        $validationEnabled = [bool]$Step.loginSuccessValidationEnabled
    }

    if (-not $validationEnabled) {
        return
    }

    $loginWindowTitle = if ($Step.PSObject.Properties.Name -contains "loginWindowTitle") { [string]$Step.loginWindowTitle } else { [string]$Step.windowTitle }
    $mainWindowTitle = if ($Step.PSObject.Properties.Name -contains "loginSuccessWindowTitle") { [string]$Step.loginSuccessWindowTitle } else { [string]$Step.windowTitle }

    $loginFallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "loginFallbackWindowTitles") {
        $loginFallbackWindowTitles = @($Step.loginFallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    $fallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "fallbackWindowTitles") {
        $fallbackWindowTitles = @($Step.fallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    $mainFallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "loginSuccessFallbackWindowTitles") {
        $mainFallbackWindowTitles = @($Step.loginSuccessFallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    $loginTitles = @($loginWindowTitle) + $loginFallbackWindowTitles
    $mainTitles = @($mainWindowTitle) + $mainFallbackWindowTitles + $fallbackWindowTitles

    $timeoutSeconds = if ($Step.PSObject.Properties.Name -contains "loginSuccessTimeoutSeconds") {
        [int]$Step.loginSuccessTimeoutSeconds
    }
    else {
        45
    }

    $intervalMs = if ($Step.PSObject.Properties.Name -contains "loginSuccessIntervalMs") {
        [int]$Step.loginSuccessIntervalMs
    }
    else {
        1000
    }

    $requireLoginWindowClosed = if ($Step.PSObject.Properties.Name -contains "loginSuccessRequireLoginWindowClosed") {
        [bool]$Step.loginSuccessRequireLoginWindowClosed
    }
    else {
        $true
    }

    $requireMainWindowVisible = if ($Step.PSObject.Properties.Name -contains "loginSuccessRequireMainWindowVisible") {
        [bool]$Step.loginSuccessRequireMainWindowVisible
    }
    else {
        $true
    }

    $controlNames = @()
    if ($Step.PSObject.Properties.Name -contains "loginSuccessControlNames") {
        $controlNames = @($Step.loginSuccessControlNames | ForEach-Object { [string]$_ })
    }

    $requireControlMatch = if ($Step.PSObject.Properties.Name -contains "loginSuccessRequireControlMatch") {
        [bool]$Step.loginSuccessRequireControlMatch
    }
    else {
        $controlNames.Count -gt 0
    }

    $processId = 0
    if ($Process) {
        try {
            $processId = [int]$Process.Id
        }
        catch {
            $processId = 0
        }
    }

    $restrictToProcess = $false
    if ($Step.PSObject.Properties.Name -contains "loginSuccessRestrictToProcess") {
        $restrictToProcess = [bool]$Step.loginSuccessRestrictToProcess
    }

    $verificationProcessId = if ($restrictToProcess) { $processId } else { 0 }

    if ($DryRun) {
        Write-LauncherLog "[DryRun] Would validate login completion for '$($Step.name)'"
        return
    }

    $maxAttempts = [Math]::Max(1, [int][Math]::Ceiling(($timeoutSeconds * 1000) / [Math]::Max(1, $intervalMs)))
    $lastLoginWindowActive = $false
    $lastMainWindowVisible = $false
    $lastControlsReady = $false

    for ($attempt = 0; $attempt -lt $maxAttempts; $attempt++) {
        $loginWindowActive = Test-AnyWindowTitleMatch -CandidateTitles $loginTitles -ProcessId $verificationProcessId
        $mainWindowVisible = Test-AnyWindowTitleMatch -CandidateTitles $mainTitles -ProcessId $verificationProcessId

        $controlsReady = $true
        if ($requireControlMatch) {
            $controlsReady = Test-WindowContainsCandidateControl -WindowTitles $mainTitles -CandidateNames $controlNames -ProcessId $verificationProcessId
        }

        $lastLoginWindowActive = $loginWindowActive
        $lastMainWindowVisible = $mainWindowVisible
        $lastControlsReady = $controlsReady

        $loginClosedOk = (-not $requireLoginWindowClosed) -or (-not $loginWindowActive)
        $mainVisibleOk = (-not $requireMainWindowVisible) -or $mainWindowVisible
        $controlsOk = (-not $requireControlMatch) -or $controlsReady

        if ($loginClosedOk -and $mainVisibleOk -and $controlsOk) {
            Write-LauncherLog "Verified login completion for '$($Step.name)'"
            return
        }

        Start-Sleep -Milliseconds $intervalMs
    }

    $diagnostic = "loginWindowActive=$lastLoginWindowActive, mainWindowVisible=$lastMainWindowVisible, controlsReady=$lastControlsReady"
    Write-LauncherLog "Login verification details for '$($Step.name)': $diagnostic" -Level "WARN"
    throw "Login verification failed for '$($Step.name)'. $diagnostic"
}

function Invoke-FirstEditControlFocus {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string[]]$PreferredNames = @(),
        [string[]]$ExcludedNames = @()
    )

    if (-not $Window) {
        return $false
    }

    $candidate = Get-PreferredEditControl -Window $Window -PreferredNames $PreferredNames -ExcludedNames $ExcludedNames
    if (-not $candidate) {
        return $false
    }

    try {
        $candidate.SetFocus()
    }
    catch {
        $null = $_
    }

    try {
        $rect = $candidate.Current.BoundingRectangle
        if (-not $rect.IsEmpty) {
            $x = [int](($rect.Left + $rect.Right) / 2)
            $y = [int](($rect.Top + $rect.Bottom) / 2)

            Add-Type -AssemblyName System.Windows.Forms
            [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point($x, $y)

            $mouse = New-Object -ComObject WScript.Shell
            $mouse.AppActivate($Window.Current.Name) | Out-Null

            Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class MouseInput {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
"@
            [void][MouseInput]::SetCursorPos($x, $y)
            [MouseInput]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
            [MouseInput]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        }
    }
    catch {
        $null = $_
    }

    return $true
}

function Invoke-EditControlValue {
    param(
        [System.Windows.Automation.AutomationElement]$Control,
        [string]$Value
    )

    if (-not $Control -or [string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    try {
        $valuePattern = $Control.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        if ($valuePattern) {
            $valuePattern.SetValue($Value)
            return $true
        }
    }
    catch {
        return $false
    }

    return $false
}

function Invoke-FirstEditControlValue {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$Value,
        [string[]]$PreferredNames = @(),
        [string[]]$ExcludedNames = @(),
        [switch]$RequirePreferredName
    )

    if (-not $Window -or [string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $candidate = Get-PreferredEditControl -Window $Window -PreferredNames $PreferredNames -ExcludedNames $ExcludedNames -RequirePreferredName:$RequirePreferredName
    if (-not $candidate) {
        return $false
    }

    return (Invoke-EditControlValue -Control $candidate -Value $Value)
}

function Invoke-AutomationElementCenter {
    param(
        [System.Windows.Automation.AutomationElement]$Element
    )

    if (-not $Element) {
        return $false
    }

    try {
        $rect = $Element.Current.BoundingRectangle
        if ($rect.IsEmpty) {
            return $false
        }

        $x = [int](($rect.Left + $rect.Right) / 2)
        $y = [int](($rect.Top + $rect.Bottom) / 2)

        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class MouseInputFallback {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll", SetLastError=true)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
"@ -ErrorAction SilentlyContinue

        [void][MouseInputFallback]::SetCursorPos($x, $y)
        [MouseInputFallback]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        [MouseInputFallback]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        return $true
    }
    catch {
        return $false
    }
}

function Find-ControlByCandidateName {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string[]]$CandidateNames
    )

    if (-not $Window -or -not $CandidateNames -or $CandidateNames.Count -eq 0) {
        return $null
    }

    $searchControls = @(
        [System.Windows.Automation.ControlType]::Button,
        [System.Windows.Automation.ControlType]::Hyperlink,
        [System.Windows.Automation.ControlType]::MenuItem,
        [System.Windows.Automation.ControlType]::Custom,
        [System.Windows.Automation.ControlType]::Text
    )

    foreach ($candidate in $CandidateNames) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $candidateNormalized = $candidate.ToLowerInvariant() -replace '[^a-z0-9]', ''
        if ([string]::IsNullOrWhiteSpace($candidateNormalized)) {
            continue
        }

        foreach ($controlType in $searchControls) {
            $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                $controlType
            )
            $controls = $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $typeCondition)

            foreach ($control in $controls) {
                $name = [string]$control.Current.Name
                if ([string]::IsNullOrWhiteSpace($name)) {
                    continue
                }

                $nameNormalized = $name.ToLowerInvariant() -replace '[^a-z0-9]', ''
                if ($nameNormalized.Contains($candidateNormalized)) {
                    return $control
                }
            }
        }
    }

    return $null
}

function Send-LoginSequence {
    param(
        [object]$Step,
        [System.Diagnostics.Process]$Process,
        [switch]$DryRun
    )

    $requireLoginFieldConfirmation = $false
    if ($Step.PSObject.Properties.Name -contains "loginFieldRequireConfirmation") {
        $requireLoginFieldConfirmation = [bool]$Step.loginFieldRequireConfirmation
    }

    $hasLoginSequence = $Step.PSObject.Properties.Name -contains "loginSequence"
    if (-not $hasLoginSequence) {
        return
    }

    $loginSequence = @($Step.loginSequence)
    if ($loginSequence.Count -eq 0) {
        return
    }

    $shell = New-Object -ComObject WScript.Shell

    $windowTitle = $Step.windowTitle
    if ([string]::IsNullOrWhiteSpace($windowTitle)) {
        throw "Step '$($Step.name)' has loginSequence but no windowTitle."
    }

    $loginWindowTitle = if ($Step.PSObject.Properties.Name -contains "loginWindowTitle") { [string]$Step.loginWindowTitle } else { $windowTitle }

    $fallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "fallbackWindowTitles") {
        $fallbackWindowTitles = @($Step.fallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    $loginFallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "loginFallbackWindowTitles") {
        $loginFallbackWindowTitles = @($Step.loginFallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    $activationFallbacks = @($loginFallbackWindowTitles + @($windowTitle) + $fallbackWindowTitles)

    $loginFallbackPreKeys = @()
    if ($Step.PSObject.Properties.Name -contains "loginFieldFallbackPreKeys") {
        $loginFallbackPreKeys = @($Step.loginFieldFallbackPreKeys | ForEach-Object { [string]$_ })
    }

    $loginFallbackClearKeys = if ($Step.PSObject.Properties.Name -contains "loginFieldFallbackClearKeys") {
        [string]$Step.loginFieldFallbackClearKeys
    }
    else {
        "^a{BACKSPACE}"
    }

    $loginFallbackValueDelayMs = if ($Step.PSObject.Properties.Name -contains "loginFieldFallbackValueDelayMs") {
        [int]$Step.loginFieldFallbackValueDelayMs
    }
    else {
        700
    }

    $timeout = if ($Step.windowTimeoutSeconds) { [int]$Step.windowTimeoutSeconds } else { 30 }
    $activated = $false
    $processId = $null
    if ($Process) {
        try {
            $processId = [int]$Process.Id
        }
        catch {
            $processId = $null
        }
    }

    for ($i = 0; $i -lt $timeout; $i++) {
        if (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $activationFallbacks) {
            $activated = $true
            break
        }

        Start-Sleep -Seconds 1
    }

    if (-not $activated) {
        if ($processId) {
            throw "Could not activate login window '$loginWindowTitle' (or process ID $processId) within $timeout seconds."
        }

        throw "Could not activate login window '$loginWindowTitle' within $timeout seconds."
    }

    $directLoginValueApplied = $false
    $loginWindowElement = $null

    if (-not $DryRun) {
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes

        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $windowControlTypeCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window
        )

        $windowTitleCandidates = @($loginWindowTitle)
        $windowTitleCandidates += $loginFallbackWindowTitles
        $windowTitleCandidates += @($windowTitle)
        $windowTitleCandidates += $fallbackWindowTitles

        $window = $null
        for ($i = 0; $i -lt 12; $i++) {
            $allWindows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $windowControlTypeCondition)

            foreach ($candidate in $windowTitleCandidates) {
                if ([string]::IsNullOrWhiteSpace($candidate)) {
                    continue
                }

                foreach ($w in $allWindows) {
                    $name = [string]$w.Current.Name
                    if ([string]::IsNullOrWhiteSpace($name)) {
                        continue
                    }

                    if ($name -eq $candidate -or $name -like "*$candidate*") {
                        $window = $w
                        break
                    }
                }

                if ($window) {
                    break
                }
            }

            if (-not $window -and $processId) {
                $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
                if ($proc -and -not [string]::IsNullOrWhiteSpace($proc.MainWindowTitle)) {
                    foreach ($w in $allWindows) {
                        if ([string]$w.Current.Name -eq $proc.MainWindowTitle) {
                            $window = $w
                            break
                        }
                    }
                }
            }

            if ($window) {
                break
            }

            Start-Sleep -Seconds 1
        }

        if ($window) {
            $loginWindowElement = $window
            $preferredNames = @('Enter employeeID', 'Enter employee ID', 'Employee ID', 'Employee', 'EmployeeId', 'Employee Id', 'Login')
            if ($Step.PSObject.Properties.Name -contains "loginFieldPreferredNames") {
                $preferredNames = @($Step.loginFieldPreferredNames | ForEach-Object { [string]$_ })
            }

            $excludedNames = @('Help', 'Search', 'Tell me', 'Find')
            if ($Step.PSObject.Properties.Name -contains "loginFieldExcludeNames") {
                $excludedNames = @($Step.loginFieldExcludeNames | ForEach-Object { [string]$_ })
            }

            $readyTimeoutSeconds = if ($Step.PSObject.Properties.Name -contains "loginFieldReadyTimeoutSeconds") { [int]$Step.loginFieldReadyTimeoutSeconds } else { 20 }
            $textboxReady = $false
            $readyEditControl = $null

            for ($attempt = 0; $attempt -lt ($readyTimeoutSeconds * 2); $attempt++) {
                $targetEdit = Get-PreferredEditControl -Window $window -PreferredNames $preferredNames -ExcludedNames $excludedNames -RequirePreferredName
                if ($targetEdit) {
                    [void](Invoke-AutomationElementCenter -Element $targetEdit)
                    try {
                        $targetEdit.SetFocus()
                    }
                    catch {
                        $null = $_
                    }

                    Start-Sleep -Milliseconds 250
                    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
                    if (Test-EditControlMatchesPreferredName -Control $focused -PreferredNames $preferredNames -ExcludedNames $excludedNames) {
                        $textboxReady = $true
                        $readyEditControl = $focused
                        break
                    }

                    if ($focused -and $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
                        $focusedName = [string]$focused.Current.Name
                        if (-not [string]::IsNullOrWhiteSpace($focusedName)) {
                            Write-LauncherLog "Focused edit '$focusedName' is not the expected Employee ID field" -Level "WARN"
                        }
                    }
                }

                Start-Sleep -Milliseconds 500
            }

            if ($textboxReady) {
                Write-LauncherLog "Confirmed Employee ID textbox cursor is active"
            }
            else {
                if ($requireLoginFieldConfirmation -and $Step.PSObject.Properties.Name -contains "loginFieldValue" -and -not [string]::IsNullOrWhiteSpace([string]$Step.loginFieldValue)) {
                    throw "Could not confirm Employee ID textbox cursor readiness."
                }

                Write-LauncherLog "Could not confirm Employee ID textbox cursor; using keyboard fallback" -Level "WARN"
            }

            Start-Sleep -Milliseconds 600

            if ($Step.PSObject.Properties.Name -contains "loginFieldValue") {
                $loginFieldValue = [string]$Step.loginFieldValue
                if (-not [string]::IsNullOrWhiteSpace($loginFieldValue)) {
                    if ($readyEditControl -and (Invoke-EditControlValue -Control $readyEditControl -Value $loginFieldValue)) {
                        $directLoginValueApplied = $true
                        Write-LauncherLog "Set Employee ID value directly in confirmed login field"
                        Start-Sleep -Milliseconds 500
                    }
                    elseif (Invoke-FirstEditControlValue -Window $window -Value $loginFieldValue -PreferredNames $preferredNames -ExcludedNames $excludedNames -RequirePreferredName) {
                        $directLoginValueApplied = $true
                        Write-LauncherLog "Set Employee ID value directly in login field"
                        Start-Sleep -Milliseconds 500
                    }
                }
            }
        }
        elseif ($requireLoginFieldConfirmation -and $Step.PSObject.Properties.Name -contains "loginFieldValue" -and -not [string]::IsNullOrWhiteSpace([string]$Step.loginFieldValue)) {
            throw "Could not locate login window for Employee ID entry. Tried titles: $($windowTitleCandidates -join '; ')"
        }
        elseif ($Step.PSObject.Properties.Name -contains "loginFieldValue" -and -not [string]::IsNullOrWhiteSpace([string]$Step.loginFieldValue)) {
            Write-LauncherLog "Could not locate login window for direct Employee ID entry; using keyboard fallback" -Level "WARN"
        }
    }

    if ($Step.PSObject.Properties.Name -contains "loginFieldValue") {
        $loginFieldValue = [string]$Step.loginFieldValue
        if (-not [string]::IsNullOrWhiteSpace($loginFieldValue) -and -not $directLoginValueApplied) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would type Employee ID value using keyboard fallback"
            }
            else {
                if ($loginWindowElement) {
                    try {
                        $loginWindowElement.SetFocus()
                    }
                    catch {
                        Write-LauncherLog "Could not focus login window directly before keyboard fallback. $($_.Exception.Message)" -Level "WARN"
                    }

                    if (Invoke-AutomationElementCenter -Element $loginWindowElement) {
                        Write-LauncherLog "Focused login window for keyboard fallback"
                        Start-Sleep -Milliseconds 250
                    }
                }

                if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $activationFallbacks)) {
                    Write-LauncherLog "Could not re-activate login window before keyboard fallback; typing into current focus" -Level "WARN"
                }

                foreach ($preKeys in $loginFallbackPreKeys) {
                    if ([string]::IsNullOrWhiteSpace($preKeys)) {
                        continue
                    }

                    Write-LauncherLog "Sending login fallback pre-keys '$preKeys'"
                    $shell.SendKeys($preKeys)
                    Start-Sleep -Milliseconds 300
                }

                if (-not [string]::IsNullOrWhiteSpace($loginFallbackClearKeys)) {
                    Write-LauncherLog "Clearing login field using keyboard fallback"
                    $shell.SendKeys($loginFallbackClearKeys)
                    Start-Sleep -Milliseconds 250
                }

                Write-LauncherLog "Typing Employee ID value using keyboard fallback"
                $shell.SendKeys($loginFieldValue)
                Start-Sleep -Milliseconds $loginFallbackValueDelayMs

                if ($loginWindowElement -and (Invoke-FirstEditControlValue -Window $loginWindowElement -Value $loginFieldValue -PreferredNames $preferredNames -ExcludedNames $excludedNames -RequirePreferredName)) {
                    Write-LauncherLog "Re-applied Employee ID value directly after keyboard fallback"
                    Start-Sleep -Milliseconds 250
                }
            }
        }
    }

    foreach ($entry in $loginSequence) {
        $value = [string]$entry.keys
        $delayMs = if ($entry.delayMs) { [int]$entry.delayMs } else { 500 }

        if (-not $DryRun) {
            if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $activationFallbacks)) {
                Write-LauncherLog "Could not re-activate login window before sending login sequence keys; continuing with current focus" -Level "WARN"
            }
        }

        Write-LauncherLog "Sending keys to '$loginWindowTitle'"
        $shell.SendKeys($value)
        Start-Sleep -Milliseconds $delayMs
    }

    if (-not $DryRun) {
        $containsEnter = $false
        foreach ($entry in $loginSequence) {
            $entryKeys = [string]$entry.keys
            if ($entryKeys -and $entryKeys.ToUpperInvariant().Contains("ENTER")) {
                $containsEnter = $true
                break
            }
        }

        if ($containsEnter) {
            $loginEnterRetryCount = if ($Step.PSObject.Properties.Name -contains "loginEnterRetryCount") {
                [int]$Step.loginEnterRetryCount
            }
            else {
                3
            }

            $loginEnterRetryDelayMs = if ($Step.PSObject.Properties.Name -contains "loginEnterRetryDelayMs") {
                [int]$Step.loginEnterRetryDelayMs
            }
            else {
                800
            }

            $loginReattemptCount = if ($Step.PSObject.Properties.Name -contains "loginReattemptCount") {
                [int]$Step.loginReattemptCount
            }
            else {
                1
            }

            $loginSubmitButtonNames = @('Enter', 'Login', 'OK')
            if ($Step.PSObject.Properties.Name -contains "loginSubmitButtonNames") {
                $loginSubmitButtonNames = @($Step.loginSubmitButtonNames | ForEach-Object { [string]$_ })
            }

            $loginSubmitButtonRetryCount = if ($Step.PSObject.Properties.Name -contains "loginSubmitButtonRetryCount") {
                [int]$Step.loginSubmitButtonRetryCount
            }
            else {
                2
            }

            $loginFailIfWindowStillActive = if ($Step.PSObject.Properties.Name -contains "loginFailIfWindowStillActive") {
                [bool]$Step.loginFailIfWindowStillActive
            }
            else {
                $true
            }

            $loginSucceeded = $false

            for ($retry = 0; $retry -lt $loginEnterRetryCount; $retry++) {
                Start-Sleep -Milliseconds $loginEnterRetryDelayMs

                if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $loginFallbackWindowTitles)) {
                    $loginSucceeded = $true
                    break
                }

                Write-LauncherLog "Login window '$loginWindowTitle' is still active; sending retry Enter ($($retry + 1)/$loginEnterRetryCount)"
                $shell.SendKeys('{ENTER}')
            }

            if (-not $loginSucceeded) {
                for ($attempt = 0; $attempt -lt $loginReattemptCount; $attempt++) {
                    Start-Sleep -Milliseconds $loginEnterRetryDelayMs
                    if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $loginFallbackWindowTitles)) {
                        $loginSucceeded = $true
                        break
                    }

                    if (-not ($Step.PSObject.Properties.Name -contains "loginFieldValue")) {
                        break
                    }

                    $loginFieldValue = [string]$Step.loginFieldValue
                    if ([string]::IsNullOrWhiteSpace($loginFieldValue)) {
                        break
                    }

                    Write-LauncherLog "Login window '$loginWindowTitle' is still active; retrying Employee ID entry ($($attempt + 1)/$loginReattemptCount)" -Level "WARN"

                    if ($loginWindowElement) {
                        [void](Invoke-FirstEditControlFocus -Window $loginWindowElement -PreferredNames $preferredNames -ExcludedNames $excludedNames)
                    }

                    if (-not [string]::IsNullOrWhiteSpace($loginFallbackClearKeys)) {
                        $shell.SendKeys($loginFallbackClearKeys)
                        Start-Sleep -Milliseconds 250
                    }

                    $shell.SendKeys($loginFieldValue)
                    Start-Sleep -Milliseconds $loginFallbackValueDelayMs
                    $shell.SendKeys('{ENTER}')
                }
            }

            if (-not $loginSucceeded -and $loginSubmitButtonNames.Count -gt 0 -and $loginSubmitButtonRetryCount -gt 0) {
                for ($buttonTry = 0; $buttonTry -lt $loginSubmitButtonRetryCount; $buttonTry++) {
                    Start-Sleep -Milliseconds $loginEnterRetryDelayMs
                    if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $loginFallbackWindowTitles)) {
                        $loginSucceeded = $true
                        break
                    }

                    $clickedSubmit = $false
                    foreach ($buttonName in $loginSubmitButtonNames) {
                        if ([string]::IsNullOrWhiteSpace($buttonName)) {
                            continue
                        }

                        try {
                            Invoke-ButtonClickByName -WindowTitle $loginWindowTitle -ButtonName $buttonName -TimeoutSeconds 5 -FallbackWindowTitles $activationFallbacks -ProcessId $processId -DryRun:$false
                            Write-LauncherLog "Clicked login submit button '$buttonName'"
                            $clickedSubmit = $true
                            break
                        }
                        catch {
                            $null = $_
                        }
                    }

                    if (-not $clickedSubmit) {
                        Write-LauncherLog "Login window '$loginWindowTitle' is still active and no submit button could be clicked" -Level "WARN"
                        break
                    }
                }
            }

            Start-Sleep -Milliseconds $loginEnterRetryDelayMs
            if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $loginWindowTitle -ProcessId $processId -FallbackWindowTitles $loginFallbackWindowTitles)) {
                $loginSucceeded = $true
            }

            if (-not $loginSucceeded -and $loginFailIfWindowStillActive) {
                throw "Login did not complete: login window '$loginWindowTitle' is still active after retries."
            }
        }
    }
}

function Wait-ForWindowAvailable {
    param(
        [string]$WindowTitle,
        [int]$TimeoutSeconds = 30
    )

    $shell = New-Object -ComObject WScript.Shell

    for ($i = 0; $i -lt $TimeoutSeconds; $i++) {
        if ($shell.AppActivate($WindowTitle)) {
            return $true
        }

        Start-Sleep -Seconds 1
    }

    return $false
}

function Invoke-ButtonClickByName {
    param(
        [string]$WindowTitle,
        [string]$ButtonName,
        [int]$TimeoutSeconds = 30,
        [string[]]$FallbackWindowTitles = @(),
        [int]$ProcessId = 0,
        [switch]$DryRun
    )

    if ($DryRun) {
        Write-LauncherLog "[DryRun] Would click button '$ButtonName' in window '$WindowTitle'"
        return
    }

    if (-not (Wait-ForWindowAvailable -WindowTitle $WindowTitle -TimeoutSeconds $TimeoutSeconds)) {
        throw "Could not activate window '$WindowTitle' within $TimeoutSeconds seconds."
    }

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $windowControlTypeCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window
    )

    $windowCondition = New-Object System.Windows.Automation.AndCondition(
        $windowControlTypeCondition,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $WindowTitle
        ))
    )

    $window = $null
    for ($i = 0; $i -lt $TimeoutSeconds; $i++) {
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $windowCondition)
        if ($window) {
            break
        }

        Start-Sleep -Seconds 1
    }

    if (-not $window) {
        $candidateTitles = @($WindowTitle) + @($FallbackWindowTitles)
        $allWindows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $windowControlTypeCondition)

        foreach ($candidate in $candidateTitles) {
            if ([string]::IsNullOrWhiteSpace($candidate)) {
                continue
            }

            foreach ($w in $allWindows) {
                $name = $w.Current.Name
                if (-not [string]::IsNullOrWhiteSpace($name) -and $name -like "*$candidate*") {
                    $window = $w
                    break
                }
            }

            if ($window) {
                break
            }
        }
    }

    if (-not $window) {
        if ($ProcessId -gt 0) {
            $allWindows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $windowControlTypeCondition)

            foreach ($candidateWindow in $allWindows) {
                if ($candidateWindow.Current.ProcessId -eq $ProcessId) {
                    $window = $candidateWindow
                    $processWindowName = [string]$candidateWindow.Current.Name
                    Write-LauncherLog "Using process window '$processWindowName' for button '$ButtonName'" -Level "WARN"
                    break
                }
            }
        }
    }

    if (-not $window) {
        $focusedElement = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($focusedElement) {
            $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
            $currentElement = $focusedElement

            while ($currentElement) {
                if ($currentElement.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
                    $focusedWindowName = [string]$currentElement.Current.Name
                    if (-not [string]::IsNullOrWhiteSpace($focusedWindowName)) {
                        $candidateTitles = @($WindowTitle) + @($FallbackWindowTitles)
                        $matchesCandidate = $false

                        foreach ($candidate in $candidateTitles) {
                            if ([string]::IsNullOrWhiteSpace($candidate)) {
                                continue
                            }

                            if ($focusedWindowName -eq $candidate -or $focusedWindowName -like "*$candidate*") {
                                $matchesCandidate = $true
                                break
                            }
                        }

                        if ($matchesCandidate) {
                            $window = $currentElement
                            Write-LauncherLog "Using focused window '$focusedWindowName' for button '$ButtonName'" -Level "WARN"
                        }
                    }

                    break
                }

                $currentElement = $walker.GetParent($currentElement)
            }
        }
    }

    if (-not $window) {
        throw "UI automation could not find window '$WindowTitle'."
    }

    $buttonCondition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button
        )),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $ButtonName
        ))
    )

    $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)

    if (-not $button) {
        $searchControls = @(
            [System.Windows.Automation.ControlType]::Button,
            [System.Windows.Automation.ControlType]::Hyperlink,
            [System.Windows.Automation.ControlType]::MenuItem,
            [System.Windows.Automation.ControlType]::Custom
        )

        $targetNameNormalized = ([string]$ButtonName).ToLowerInvariant() -replace '[^a-z0-9]', ''
        $targetTokens = @([regex]::Matches(([string]$ButtonName).ToLowerInvariant(), '[a-z0-9]+') | ForEach-Object { $_.Value })

        foreach ($controlType in $searchControls) {
            $typeCondition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                $controlType
            )

            $controls = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $typeCondition)
            foreach ($control in $controls) {
                $candidateName = [string]$control.Current.Name
                if ([string]::IsNullOrWhiteSpace($candidateName)) {
                    continue
                }

                $candidateNormalized = $candidateName.ToLowerInvariant() -replace '[^a-z0-9]', ''

                if ($candidateNormalized -eq $targetNameNormalized -or $candidateNormalized.Contains($targetNameNormalized)) {
                    $button = $control
                    break
                }

                $matchedAllTokens = $true
                foreach ($token in $targetTokens) {
                    if (-not $candidateNormalized.Contains($token)) {
                        $matchedAllTokens = $false
                        break
                    }
                }

                if ($matchedAllTokens) {
                    $button = $control
                    break
                }
            }

            if ($button) {
                break
            }
        }
    }

    if (-not $button) {
        $diagnosticNames = @()
        $allControls = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($control in $allControls) {
            $name = [string]$control.Current.Name
            if ([string]::IsNullOrWhiteSpace($name)) {
                continue
            }

            $nameLower = $name.ToLowerInvariant()
            if ($nameLower.Contains('add') -or $nameLower.Contains('update') -or $nameLower.Contains('table')) {
                if (-not ($diagnosticNames -contains $name)) {
                    $diagnosticNames += $name
                }
            }

            if ($diagnosticNames.Count -ge 12) {
                break
            }
        }

        if ($diagnosticNames.Count -gt 0) {
            throw "Button '$ButtonName' was not found in window '$WindowTitle'. Similar control names: $($diagnosticNames -join '; ')"
        }

        throw "Button '$ButtonName' was not found in window '$WindowTitle'."
    }

    $invoked = $false
    try {
        $invokePattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        if ($invokePattern) {
            $invokePattern.Invoke()
            $invoked = $true
        }
    }
    catch {
        $invoked = $false
    }

    if (-not $invoked) {
        try {
            $selectionPattern = $button.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            if ($selectionPattern) {
                $selectionPattern.Select()
                $invoked = $true
            }
        }
        catch {
            $invoked = $false
        }
    }

    if (-not $invoked) {
        if (Invoke-AutomationElementCenter -Element $button) {
            $invoked = $true
        }
    }

    if (-not $invoked) {
        throw "Control '$ButtonName' was found but could not be invoked."
    }

    Write-LauncherLog "Clicked button '$ButtonName' in '$WindowTitle'"
}

function Invoke-UpdateTableFlow {
    param(
        [object]$Step,
        [System.Diagnostics.Process]$Process,
        [switch]$DryRun
    )

    if (-not ($Step.PSObject.Properties.Name -contains "updateTableFlow")) {
        return
    }

    $flow = $Step.updateTableFlow
    if (-not $flow) {
        return
    }

    $mainWindowTitle = if ($flow.PSObject.Properties.Name -contains "mainWindowTitle") { [string]$flow.mainWindowTitle } else { [string]$Step.windowTitle }
    $defaultActionDelayMs = if ($flow.PSObject.Properties.Name -contains "actionDelayMs") { [int]$flow.actionDelayMs } else { 1200 }
    $processId = 0
    if ($Process) {
        try {
            $processId = [int]$Process.Id
        }
        catch {
            $processId = 0
        }
    }
    $fallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "fallbackWindowTitles") {
        $fallbackWindowTitles = @($Step.fallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    if ($flow.PSObject.Properties.Name -contains "postEmployeeLoginButtonName") {
        $enterButtonName = [string]$flow.postEmployeeLoginButtonName
        $enterButtonClicks = if ($flow.PSObject.Properties.Name -contains "postEmployeeLoginButtonClicks") { [int]$flow.postEmployeeLoginButtonClicks } else { 1 }

        if (-not [string]::IsNullOrWhiteSpace($enterButtonName) -and $enterButtonClicks -gt 0) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would click post-login button '$enterButtonName' $enterButtonClicks time(s)"
            }
            else {
                for ($i = 0; $i -lt $enterButtonClicks; $i++) {
                    Invoke-ButtonClickByName -WindowTitle $mainWindowTitle -ButtonName $enterButtonName -TimeoutSeconds 45 -FallbackWindowTitles $fallbackWindowTitles -ProcessId $processId -DryRun:$DryRun
                    Start-Sleep -Milliseconds $defaultActionDelayMs
                }
            }
        }
    }

    if ($flow.PSObject.Properties.Name -contains "postEmployeeLoginSequence") {
        $postLoginSequence = @($flow.postEmployeeLoginSequence)
        if ($postLoginSequence.Count -gt 0) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would send post-employee-login key sequence to '$mainWindowTitle'"
            }
            else {
                $shell = New-Object -ComObject WScript.Shell
                if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $mainWindowTitle -ProcessId 0 -FallbackWindowTitles $fallbackWindowTitles)) {
                    for ($i = 0; $i -lt 30; $i++) {
                        if (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $mainWindowTitle -ProcessId 0 -FallbackWindowTitles $fallbackWindowTitles) {
                            break
                        }

                        Start-Sleep -Seconds 1
                    }
                }

                if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $mainWindowTitle -ProcessId 0 -FallbackWindowTitles $fallbackWindowTitles)) {
                    throw "Could not activate window '$mainWindowTitle' for post-employee-login keys."
                }

                foreach ($entry in $postLoginSequence) {
                    $value = [string]$entry.keys
                    $delayMs = if ($entry.delayMs) { [int]$entry.delayMs } else { $defaultActionDelayMs }

                    $shell.SendKeys($value)
                    Write-LauncherLog "Sent post-employee-login keys to '$mainWindowTitle'"
                    Start-Sleep -Milliseconds $delayMs
                }
            }
        }
    }
    elseif ($flow.PSObject.Properties.Name -contains "postEmployeeLoginKeys") {
        $keys = [string]$flow.postEmployeeLoginKeys
        if (-not [string]::IsNullOrWhiteSpace($keys)) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would send post-employee-login keys to '$mainWindowTitle'"
            }
            else {
                if (-not (Wait-ForWindowAvailable -WindowTitle $mainWindowTitle -TimeoutSeconds 30)) {
                    throw "Could not activate window '$mainWindowTitle' for post-employee-login keys."
                }

                $shell = New-Object -ComObject WScript.Shell
                $shell.SendKeys($keys)
                Write-LauncherLog "Sent post-employee-login keys to '$mainWindowTitle'"
                Start-Sleep -Milliseconds $defaultActionDelayMs
            }
        }
    }

    $updateButtonCandidates = @()
    if ($flow.PSObject.Properties.Name -contains "updateTableButtonNames") {
        $updateButtonCandidates = @($flow.updateTableButtonNames | ForEach-Object { [string]$_ })
    }
    elseif ($flow.PSObject.Properties.Name -contains "updateTableButtonName") {
        $updateButtonCandidates = @([string]$flow.updateTableButtonName)
    }

    if ($updateButtonCandidates.Count -gt 0) {
        if ($DryRun) {
            Write-LauncherLog "[DryRun] Would click update table button '$($updateButtonCandidates[0])' in window '$mainWindowTitle'"
        }
        else {
            $postLoginWaitSeconds = if ($flow.PSObject.Properties.Name -contains "postLoginWaitSeconds") { [int]$flow.postLoginWaitSeconds } else { 0 }
            if ($postLoginWaitSeconds -gt 0) {
                Write-LauncherLog "Waiting $postLoginWaitSeconds second(s) for login completion"
                Start-Sleep -Seconds $postLoginWaitSeconds
            }

            $readyTimeoutSeconds = if ($flow.PSObject.Properties.Name -contains "loginReadyTimeoutSeconds") { [int]$flow.loginReadyTimeoutSeconds } else { 60 }
            $readyControlNames = if ($flow.PSObject.Properties.Name -contains "loginReadyControlNames") {
                @($flow.loginReadyControlNames | ForEach-Object { [string]$_ })
            }
            else {
                @($updateButtonCandidates)
            }

            $shell = New-Object -ComObject WScript.Shell
            if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $mainWindowTitle -ProcessId 0 -FallbackWindowTitles $fallbackWindowTitles)) {
                throw "Could not activate '$mainWindowTitle' while waiting for login-ready state."
            }

            Add-Type -AssemblyName UIAutomationClient
            Add-Type -AssemblyName UIAutomationTypes
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $window = $null
            for ($i = 0; $i -lt 15; $i++) {
                $window = $root.FindFirst(
                    [System.Windows.Automation.TreeScope]::Children,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::NameProperty,
                        $mainWindowTitle
                    ))
                )
                if ($window) { break }
                Start-Sleep -Milliseconds 500
            }

            if ($window) {
                $ready = $false
                for ($i = 0; $i -lt $readyTimeoutSeconds; $i++) {
                    if (Find-ControlByCandidateName -Window $window -CandidateNames $readyControlNames) {
                        $ready = $true
                        break
                    }
                    Start-Sleep -Seconds 1
                }

                if (-not $ready) {
                    Write-LauncherLog "Login-ready controls were not detected within $readyTimeoutSeconds second(s); continuing with fallback attempts" -Level "WARN"
                }
            }

            $clicked = $false
            $lastError = $null

            foreach ($candidate in $updateButtonCandidates) {
                if ([string]::IsNullOrWhiteSpace($candidate)) {
                    continue
                }

                try {
                    Invoke-ButtonClickByName -WindowTitle $mainWindowTitle -ButtonName $candidate -TimeoutSeconds 45 -FallbackWindowTitles $fallbackWindowTitles -ProcessId $processId -DryRun:$DryRun
                    $clicked = $true
                    break
                }
                catch {
                    $lastError = $_.Exception.Message
                }
            }

            if (-not $clicked) {
                $fallbackKeys = @()
                if ($flow.PSObject.Properties.Name -contains "updateTableFallbackKeys") {
                    $fallbackKeys = @($flow.updateTableFallbackKeys | ForEach-Object { [string]$_ })
                }

                if ($fallbackKeys.Count -gt 0) {
                    $shell = New-Object -ComObject WScript.Shell
                    if (-not (Test-StepWindowActivation -Shell $shell -PrimaryWindowTitle $mainWindowTitle -ProcessId 0 -FallbackWindowTitles $fallbackWindowTitles)) {
                        throw "Could not activate '$mainWindowTitle' before running update table fallback keys."
                    }

                    foreach ($keys in $fallbackKeys) {
                        if ([string]::IsNullOrWhiteSpace($keys)) {
                            continue
                        }

                        $shell.SendKeys($keys)
                        Write-LauncherLog "Sent update table fallback keys '$keys'"
                        Start-Sleep -Milliseconds $defaultActionDelayMs
                    }
                }
                elseif ($lastError) {
                    throw "Could not click any update table button candidate. Last error: $lastError"
                }
                else {
                    throw "Could not click any update table button candidate."
                }
            }

            $passwordWindowTitles = @()
            if ($flow.PSObject.Properties.Name -contains "passwordWindowTitles") {
                $passwordWindowTitles = @($flow.passwordWindowTitles | ForEach-Object { [string]$_ })
            }
            elseif ($flow.PSObject.Properties.Name -contains "passwordWindowTitle") {
                $passwordWindowTitles = @([string]$flow.passwordWindowTitle)
            }

            $passwordWindowFallbackTitles = @()
            if ($flow.PSObject.Properties.Name -contains "passwordWindowFallbackTitles") {
                $passwordWindowFallbackTitles = @($flow.passwordWindowFallbackTitles | ForEach-Object { [string]$_ })
            }

            $passwordWaitTimeoutSeconds = if ($flow.PSObject.Properties.Name -contains "passwordWindowTimeoutSeconds") { [int]$flow.passwordWindowTimeoutSeconds } else { 45 }

            $passwordShell = New-Object -ComObject WScript.Shell
            $passwordWindowActivated = $false
            foreach ($candidateTitle in @($passwordWindowTitles + $passwordWindowFallbackTitles + @($mainWindowTitle) + $fallbackWindowTitles)) {
                if ([string]::IsNullOrWhiteSpace($candidateTitle)) {
                    continue
                }

                for ($i = 0; $i -lt $passwordWaitTimeoutSeconds; $i++) {
                    if ($passwordShell.AppActivate($candidateTitle)) {
                        $passwordWindowActivated = $true
                        break
                    }

                    Start-Sleep -Seconds 1
                }

                if ($passwordWindowActivated) {
                    break
                }
            }

            if (-not $passwordWindowActivated) {
                $focusedElement = [System.Windows.Automation.AutomationElement]::FocusedElement
                if ($focusedElement) {
                    try {
                        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
                        $currentElement = $focusedElement
                        while ($currentElement) {
                            if ($currentElement.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window) {
                                $passwordWindowActivated = $true
                                break
                            }
                            $currentElement = $walker.GetParent($currentElement)
                        }
                    }
                    catch {
                        $null = $_
                    }
                }
            }

            if (-not $passwordWindowActivated) {
                Write-LauncherLog "Could not activate a password prompt for update table; typing into current focus" -Level "WARN"
            }

            Start-Sleep -Milliseconds $defaultActionDelayMs
        }
    }

    if ($flow.PSObject.Properties.Name -contains "password") {
        $password = [string]$flow.password
        if (-not [string]::IsNullOrWhiteSpace($password)) {
            $passwordWindowTitle = if ($flow.PSObject.Properties.Name -contains "passwordWindowTitle") { [string]$flow.passwordWindowTitle } else { [string]$mainWindowTitle }
            $passwordEnterKeys = if ($flow.PSObject.Properties.Name -contains "passwordEnterKeys") { [string]$flow.passwordEnterKeys } else { "{ENTER}" }

            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would enter update table password in '$passwordWindowTitle'"
            }
            else {
                $shell = New-Object -ComObject WScript.Shell
                $passwordFallbacks = @($passwordWindowTitles + $passwordWindowFallbackTitles + @($fallbackWindowTitles) + @($mainWindowTitle)) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
                $activated = $false
                foreach ($candidateTitle in $passwordFallbacks) {
                    if ($shell.AppActivate($candidateTitle)) {
                        $activated = $true
                        break
                    }
                }

                if (-not $activated) {
                    Write-LauncherLog "Could not activate password window '$passwordWindowTitle'; typing into current focus" -Level "WARN"
                }

                $shell.SendKeys($password)
                Start-Sleep -Milliseconds $defaultActionDelayMs
                $shell.SendKeys($passwordEnterKeys)
                Write-LauncherLog "Submitted update table password"
            }
        }
    }

    $waitSeconds = if ($flow.PSObject.Properties.Name -contains "waitAfterPasswordSeconds") { [int]$flow.waitAfterPasswordSeconds } else { 0 }
    if ($waitSeconds -gt 0) {
        if ($DryRun) {
            Write-LauncherLog "[DryRun] Would wait $waitSeconds seconds for update process to finish"
        }
        else {
            Write-LauncherLog "Waiting $waitSeconds seconds for update process to finish"
            Start-Sleep -Seconds $waitSeconds
        }
    }
}

function Wait-ForWindowToClose {
    param(
        [object]$Step,
        [switch]$DryRun
    )

    if (-not ($Step.PSObject.Properties.Name -contains "waitForWindowToCloseTitle")) {
        return
    }

    $windowTitle = [string]$Step.waitForWindowToCloseTitle
    if ([string]::IsNullOrWhiteSpace($windowTitle)) {
        return
    }

    $timeoutSeconds = if ($Step.PSObject.Properties.Name -contains "waitForWindowToCloseTimeoutSeconds") {
        [int]$Step.waitForWindowToCloseTimeoutSeconds
    }
    else {
        180
    }

    # How long to look for the updater window before assuming no update is pending.
    # When omitted the full timeout applies (backward-compatible).
    $detectionTimeoutSeconds = if ($Step.PSObject.Properties.Name -contains "waitForWindowToCloseDetectionTimeoutSeconds") {
        [int]$Step.waitForWindowToCloseDetectionTimeoutSeconds
    }
    else {
        $timeoutSeconds
    }

    if ($DryRun) {
        Write-LauncherLog "[DryRun] Would check for updater window '$windowTitle' (detect up to ${detectionTimeoutSeconds}s, close up to ${timeoutSeconds}s)"
        return
    }

    $shell = New-Object -ComObject WScript.Shell
    Write-LauncherLog "Checking for updater window '$windowTitle' (up to $detectionTimeoutSeconds second(s))"

    # Phase 1 – detect: wait for the updater window to appear.
    $sawWindow = $false
    for ($i = 0; $i -lt $detectionTimeoutSeconds; $i++) {
        if ($shell.AppActivate($windowTitle)) {
            $sawWindow = $true
            Write-LauncherLog "Updater window '$windowTitle' detected; waiting for update to complete"
            break
        }
        Start-Sleep -Seconds 1
    }

    if (-not $sawWindow) {
        Write-LauncherLog "Updater window '$windowTitle' not detected; no update pending, continuing"
        return
    }

    # Phase 2 – wait for close: update is in progress, wait for it to finish.
    for ($i = 0; $i -lt $timeoutSeconds; $i++) {
        if (-not $shell.AppActivate($windowTitle)) {
            Write-LauncherLog "Updater window '$windowTitle' has closed; update complete"
            return
        }
        Start-Sleep -Seconds 1
    }

    throw "Updater window '$windowTitle' did not close within $timeoutSeconds seconds."
}

function Invoke-AccessSqlStep {
    param(
        [object]$Step,
        [string]$ConfigDirectory,
        [switch]$DryRun
    )

    $dbPath = Resolve-StepPath -Path ([string]$Step.databasePath) -ConfigDirectory $ConfigDirectory

    $sqlStatements = @($Step.sql)
    if ($sqlStatements.Count -eq 0) {
        throw "No SQL statements were provided for step '$($Step.name)'."
    }

    $connectionString = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$dbPath;Persist Security Info=False;"

    if ($DryRun) {
        Write-LauncherLog "[DryRun] Would execute $($sqlStatements.Count) SQL statement(s) against $dbPath"
        return
    }

    if (-not (Test-Path -Path $dbPath)) {
        throw "Database not found for step '$($Step.name)': $dbPath"
    }

    Write-LauncherLog "Executing Access SQL for '$($Step.name)'"
    $connection = New-Object System.Data.OleDb.OleDbConnection($connectionString)

    try {
        $connection.Open()

        foreach ($sql in $sqlStatements) {
            $cmd = $connection.CreateCommand()
            $cmd.CommandText = [string]$sql
            [void]$cmd.ExecuteNonQuery()
        }
    }
    finally {
        if ($connection.State -eq [System.Data.ConnectionState]::Open) {
            $connection.Close()
        }
        $connection.Dispose()
    }
}

function Invoke-MinimizeLaunchedWindow {
    param(
        [object]$Step,
        [System.Diagnostics.Process]$Process,
        [switch]$AfterCompletion,
        [switch]$DryRun
    )

    $shouldMinimize = $false
    if ($AfterCompletion) {
        if ($Step.PSObject.Properties.Name -contains "minimizeAfterCompletion") {
            $shouldMinimize = [bool]$Step.minimizeAfterCompletion
        }
        else {
            # Default to minimizing launch-type steps once their automation is complete.
            $shouldMinimize = $true
        }
    }
    elseif ($Step.PSObject.Properties.Name -contains "minimizeAfterLaunch") {
        $shouldMinimize = [bool]$Step.minimizeAfterLaunch
    }

    if (-not $shouldMinimize) {
        return
    }

    $delaySeconds = if ($AfterCompletion -and $Step.PSObject.Properties.Name -contains "minimizeAfterCompletionDelaySeconds") {
        [int]$Step.minimizeAfterCompletionDelaySeconds
    }
    elseif ($Step.PSObject.Properties.Name -contains "minimizeAfterLaunchDelaySeconds") {
        [int]$Step.minimizeAfterLaunchDelaySeconds
    }
    else {
        0
    }

    if ($delaySeconds -gt 0) {
        if ($DryRun) {
            if ($AfterCompletion) {
                Write-LauncherLog "[DryRun] Would wait $delaySeconds second(s) before minimizing '$($Step.name)' after completion"
            }
            else {
                Write-LauncherLog "[DryRun] Would wait $delaySeconds second(s) before minimizing '$($Step.name)'"
            }
        }
        else {
            Start-Sleep -Seconds $delaySeconds
        }
    }

    if ($DryRun) {
        if ($AfterCompletion) {
            Write-LauncherLog "[DryRun] Would minimize '$($Step.name)' after completion"
        }
        else {
            Write-LauncherLog "[DryRun] Would minimize '$($Step.name)' before continuing"
        }
        return
    }

    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowMinimize {
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
"@ -ErrorAction SilentlyContinue

    $programPathValue = if ($Step.PSObject.Properties.Name -contains "programPath") { [string]$Step.programPath } else { "" }
    $isOutlookStep = ([string]$Step.name -like "*Outlook*") -or ($programPathValue -match "(?i)olk\.exe|outlook")
    $maximizePauseMs = if ($Step.PSObject.Properties.Name -contains "maximizeBeforeMinimizeDelayMs") {
        [int]$Step.maximizeBeforeMinimizeDelayMs
    }
    elseif ($isOutlookStep) {
        1200
    }
    else {
        300
    }

    $timeoutSeconds = if ($Step.PSObject.Properties.Name -contains "minimizeWindowTimeoutSeconds") {
        [int]$Step.minimizeWindowTimeoutSeconds
    }
    else {
        15
    }

    if ($Process) {
        for ($i = 0; $i -lt $timeoutSeconds; $i++) {
            try { $Process.Refresh() } catch { $null = $_ }
            $hwnd = $null
            try { $hwnd = $Process.MainWindowHandle } catch { $null = $_ }
            if ($hwnd -and $hwnd -ne [IntPtr]::Zero) {
                if ($Step.PSObject.Properties.Name -contains "windowStyle" -and [string]$Step.windowStyle -eq "Maximized") {
                    [void][WindowMinimize]::ShowWindowAsync($hwnd, 3)
                    Start-Sleep -Milliseconds $maximizePauseMs
                }
                [void][WindowMinimize]::ShowWindowAsync($hwnd, 6)
                Write-LauncherLog "Minimized '$($Step.name)'"
                return
            }

            Start-Sleep -Seconds 1
        }
    }

    $relatedProcesses = @()
    if ($Process) {
        try {
            $processById = Get-Process -Id $Process.Id -ErrorAction SilentlyContinue
            if ($processById) {
                $relatedProcesses += $processById
            }
        }
        catch {
            $null = $_
        }
    }

    if ($isOutlookStep) {
        $relatedProcesses += @(Get-Process -Name "OUTLOOK" -ErrorAction SilentlyContinue)
    }

    $relatedProcesses = @($relatedProcesses | Where-Object {
        if (-not $_) { return $false }
        try { $h = $_.MainWindowHandle; return ($h -and $h -ne [IntPtr]::Zero) } catch { return $false }
    } | Sort-Object -Property StartTime -Descending)
    if ($relatedProcesses.Count -gt 0) {
        $targetProcess = $relatedProcesses[0]
        $targetHwnd = $null
        try { $targetHwnd = $targetProcess.MainWindowHandle } catch { $null = $_ }
        if ($targetHwnd -and $targetHwnd -ne [IntPtr]::Zero) {
            if ($Step.PSObject.Properties.Name -contains "windowStyle" -and [string]$Step.windowStyle -eq "Maximized") {
                [void][WindowMinimize]::ShowWindowAsync($targetHwnd, 3)
                Start-Sleep -Milliseconds $maximizePauseMs
            }
            [void][WindowMinimize]::ShowWindowAsync($targetHwnd, 6)
            Write-LauncherLog "Minimized '$($Step.name)' using process '$($targetProcess.ProcessName)'"
            return
        }
    }

    $titleCandidates = @()
    if ($Step.PSObject.Properties.Name -contains "minimizeWindowTitles") {
        $titleCandidates += @($Step.minimizeWindowTitles | ForEach-Object { [string]$_ })
    }

    if ($Step.PSObject.Properties.Name -contains "windowTitle") {
        $titleCandidates += [string]$Step.windowTitle
    }

    if ($Step.PSObject.Properties.Name -contains "fallbackWindowTitles") {
        $titleCandidates += @($Step.fallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    if ($Step.PSObject.Properties.Name -contains "name") {
        $titleCandidates += [string]$Step.name
    }

    $titleCandidates = @($titleCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

    if ($titleCandidates.Count -gt 0) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($candidate in $titleCandidates) {
            if ($shell.AppActivate($candidate)) {
                Start-Sleep -Milliseconds 250
                $foregroundWindowHandle = [WindowMinimize]::GetForegroundWindow()
                $foregroundWindowHandle = $null
                try { $foregroundWindowHandle = [WindowMinimize]::GetForegroundWindow() } catch { $null = $_ }
                if ($foregroundWindowHandle -and $foregroundWindowHandle -ne [IntPtr]::Zero) {
                    if ($Step.PSObject.Properties.Name -contains "windowStyle" -and [string]$Step.windowStyle -eq "Maximized") {
                        [void][WindowMinimize]::ShowWindowAsync($foregroundWindowHandle, 3)
                        Start-Sleep -Milliseconds $maximizePauseMs
                    }

                    [void][WindowMinimize]::ShowWindowAsync($foregroundWindowHandle, 6)
                    Write-LauncherLog "Minimized '$($Step.name)' using title '$candidate'"
                    return
                }
            }
        }
    }

    Write-LauncherLog "Could not find a window handle to minimize '$($Step.name)'" -Level "WARN"
}

function Invoke-MinimizeWindowTitle {
    param(
        [string]$StepName,
        [string[]]$TitleCandidates,
        [switch]$DryRun
    )

    $uniqueTitles = @($TitleCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    if ($uniqueTitles.Count -eq 0) {
        return
    }

    if ($DryRun) {
        foreach ($candidate in $uniqueTitles) {
            Write-LauncherLog "[DryRun] Would minimize additional window for '$StepName' using title '$candidate'"
        }
        return
    }

    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowMinimize {
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
"@ -ErrorAction SilentlyContinue

    $shell = New-Object -ComObject WScript.Shell
    foreach ($candidate in $uniqueTitles) {
        if (-not $shell.AppActivate($candidate)) {
            continue
        }

        Start-Sleep -Milliseconds 250
        $foregroundWindowHandle = $null
        try { $foregroundWindowHandle = [WindowMinimize]::GetForegroundWindow() } catch { $null = $_ }
        if ($foregroundWindowHandle -and $foregroundWindowHandle -ne [IntPtr]::Zero) {
            [void][WindowMinimize]::ShowWindowAsync($foregroundWindowHandle, 6)
            Write-LauncherLog "Minimized additional window for '$StepName' using title '$candidate'"
        }
    }
}

function Set-LockKeysOn {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [bool]$EnsureCapsLockOn = $false,
        [bool]$EnsureNumLockOn = $false,
        [switch]$DryRun
    )

    if (-not $EnsureCapsLockOn -and -not $EnsureNumLockOn) {
        Write-LauncherLog "Leaving Caps Lock and Num Lock unchanged"
        return
    }

    if ($DryRun) {
        if ($EnsureCapsLockOn) {
            Write-LauncherLog "[DryRun] Would ensure Caps Lock is ON"
        }
        if ($EnsureNumLockOn) {
            Write-LauncherLog "[DryRun] Would ensure Num Lock is ON"
        }
        return
    }

    if (-not $PSCmdlet.ShouldProcess("Keyboard lock keys", "Ensure Caps Lock and Num Lock are ON")) {
        return
    }

    $shell = New-Object -ComObject WScript.Shell

    if ($EnsureCapsLockOn -and -not [Console]::CapsLock) {
        $shell.SendKeys('{CAPSLOCK}')
        Start-Sleep -Milliseconds 150
    }

    if ($EnsureNumLockOn -and -not [Console]::NumberLock) {
        $shell.SendKeys('{NUMLOCK}')
        Start-Sleep -Milliseconds 150
    }

    if ($EnsureCapsLockOn) {
        Write-LauncherLog "Caps Lock is ON"
    }
    if ($EnsureNumLockOn) {
        Write-LauncherLog "Num Lock is ON"
    }
}

function Invoke-MoveWindowToMonitor {
    param(
        [object]$Step,
        [System.Diagnostics.Process]$Process,
        [switch]$BeforeLogin,
        [switch]$DryRun
    )

    $shouldMoveBeforeLogin = $false
    if ($Step.PSObject.Properties.Name -contains "moveToMonitorBeforeLogin") {
        $shouldMoveBeforeLogin = [bool]$Step.moveToMonitorBeforeLogin
    }

    $shouldMoveAfterLogin = $false
    if ($Step.PSObject.Properties.Name -contains "moveToMonitorAfterLogin") {
        $shouldMoveAfterLogin = [bool]$Step.moveToMonitorAfterLogin
    }

    $shouldMove = if ($BeforeLogin) { $shouldMoveBeforeLogin } else { $shouldMoveAfterLogin }
    if (-not $shouldMove) {
        return
    }

    $targetMonitor = if ($Step.PSObject.Properties.Name -contains "targetMonitor") { [string]$Step.targetMonitor } else { "Left" }
    $mainWindowTitle = if ($Step.PSObject.Properties.Name -contains "loginSuccessWindowTitle") { [string]$Step.loginSuccessWindowTitle } else { [string]$Step.windowTitle }

    $fallbackWindowTitles = @()
    if ($Step.PSObject.Properties.Name -contains "loginSuccessFallbackWindowTitles") {
        $fallbackWindowTitles += @($Step.loginSuccessFallbackWindowTitles | ForEach-Object { [string]$_ })
    }
    if ($Step.PSObject.Properties.Name -contains "fallbackWindowTitles") {
        $fallbackWindowTitles += @($Step.fallbackWindowTitles | ForEach-Object { [string]$_ })
    }

    $windowGroups = @(
        @{
            Label  = [string]$Step.name
            Titles = @($mainWindowTitle) + $fallbackWindowTitles
        }
    )

    $searchTimeoutSeconds = if ($Step.PSObject.Properties.Name -contains "moveWindowSearchTimeoutSeconds") {
        [int]$Step.moveWindowSearchTimeoutSeconds
    }
    else {
        25
    }

    $searchIntervalMs = if ($Step.PSObject.Properties.Name -contains "moveWindowSearchIntervalMs") {
        [int]$Step.moveWindowSearchIntervalMs
    }
    else {
        1000
    }

    if (-not $BeforeLogin -and $Step.PSObject.Properties.Name -contains "additionalWindowsToMoveAfterLogin") {
        foreach ($windowSpec in @($Step.additionalWindowsToMoveAfterLogin)) {
            if (-not $windowSpec) {
                continue
            }

            $windowLabel = if ($windowSpec.PSObject.Properties.Name -contains "name") { [string]$windowSpec.name } else { "Additional Window" }
            $windowTitles = @()
            if ($windowSpec.PSObject.Properties.Name -contains "windowTitle") {
                $windowTitles += [string]$windowSpec.windowTitle
            }
            if ($windowSpec.PSObject.Properties.Name -contains "fallbackWindowTitles") {
                $windowTitles += @($windowSpec.fallbackWindowTitles | ForEach-Object { [string]$_ })
            }

            $windowTitles = @($windowTitles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($windowTitles.Count -gt 0) {
                $windowGroups += @{
                    Label  = $windowLabel
                    Titles = $windowTitles
                }
            }
        }
    }

    if ($DryRun) {
        foreach ($windowGroup in $windowGroups) {
            Write-LauncherLog "[DryRun] Would move '$($windowGroup.Label)' to the $targetMonitor monitor"
        }
        return
    }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class WindowPlacement {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
"@ -ErrorAction SilentlyContinue

    $screens = [System.Windows.Forms.Screen]::AllScreens
    if (-not $screens -or $screens.Count -lt 2) {
        Write-LauncherLog "Could not move '$($Step.name)' because a second monitor was not detected" -Level "WARN"
        return
    }

    $targetScreen = switch ($targetMonitor.ToUpperInvariant()) {
        "LEFT" { $screens | Sort-Object { $_.WorkingArea.Left } | Select-Object -First 1 }
        "RIGHT" { $screens | Sort-Object { $_.WorkingArea.Left } | Select-Object -Last 1 }
        default { $screens | Sort-Object { $_.WorkingArea.Left } | Select-Object -First 1 }
    }

    if (-not $targetScreen) {
        Write-LauncherLog "Could not resolve target monitor '$targetMonitor' for '$($Step.name)'" -Level "WARN"
        return
    }

    $workingArea = $targetScreen.WorkingArea

    function Get-TopLevelWindows {
        $windows = New-Object System.Collections.Generic.List[object]
        $callback = [WindowPlacement+EnumWindowsProc]{
            param([IntPtr]$hWnd, [IntPtr]$lParam)

            $null = $lParam

            if (-not [WindowPlacement]::IsWindowVisible($hWnd)) {
                return $true
            }

            $builder = New-Object System.Text.StringBuilder 512
            [void][WindowPlacement]::GetWindowText($hWnd, $builder, $builder.Capacity)
            $title = $builder.ToString()
            if ([string]::IsNullOrWhiteSpace($title)) {
                return $true
            }

            $windowPid = [uint32]0
            [void][WindowPlacement]::GetWindowThreadProcessId($hWnd, [ref]$windowPid)
            $windows.Add([pscustomobject]@{
                Handle = $hWnd
                Title  = $title
                Pid    = [int]$windowPid
            })

            return $true
        }

        [void][WindowPlacement]::EnumWindows($callback, [IntPtr]::Zero)
        return $windows
    }

    foreach ($windowGroup in $windowGroups) {
        $candidateTitles = @($windowGroup.Titles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $windowHandle = [IntPtr]::Zero

        $maxAttempts = [Math]::Max(1, [int][Math]::Ceiling(($searchTimeoutSeconds * 1000) / [Math]::Max(1, $searchIntervalMs)))
        for ($attempt = 0; $attempt -lt $maxAttempts; $attempt++) {
            $topWindows = Get-TopLevelWindows
            $processId = 0
            if ($Process) {
                try {
                    $processId = [int]$Process.Id
                }
                catch {
                    $processId = 0
                }
            }

            foreach ($candidate in $candidateTitles) {
                $matchedWindow = $topWindows | Where-Object {
                    $_.Title -eq $candidate -or $_.Title -like "*$candidate*"
                } | Select-Object -First 1

                if ($matchedWindow) {
                    $windowHandle = $matchedWindow.Handle
                    break
                }
            }

            if ($windowHandle -eq [IntPtr]::Zero -and $processId -gt 0) {
                $sameProcessWindow = $topWindows | Where-Object {
                    $_.Pid -eq $processId -and ($_.Title -eq $mainWindowTitle -or $_.Title -like "*$mainWindowTitle*")
                } | Select-Object -First 1

                if ($sameProcessWindow) {
                    $windowHandle = $sameProcessWindow.Handle
                }
            }

            if ($windowHandle -eq [IntPtr]::Zero -and $Process -and $windowGroup.Label -eq [string]$Step.name) {
                try {
                    $Process.Refresh()
                    if ($Process.MainWindowHandle -ne 0) {
                        $windowHandle = $Process.MainWindowHandle
                    }
                }
                catch {
                    $null = $_
                }
            }

            if ($windowHandle -ne [IntPtr]::Zero) {
                break
            }

            Start-Sleep -Milliseconds $searchIntervalMs
        }

        if ($windowHandle -eq [IntPtr]::Zero) {
            Write-LauncherLog "Could not find a window to move for '$($windowGroup.Label)' within $searchTimeoutSeconds second(s)" -Level "WARN"
            continue
        }

        [void][WindowPlacement]::ShowWindowAsync($windowHandle, 9)

        $rect = New-Object WindowPlacement+RECT
        $windowWidth = 1200
        $windowHeight = 800
        if ([WindowPlacement]::GetWindowRect($windowHandle, [ref]$rect)) {
            $windowWidth = [Math]::Max(200, $rect.Right - $rect.Left)
            $windowHeight = [Math]::Max(200, $rect.Bottom - $rect.Top)
        }

        $windowWidth = [Math]::Min($windowWidth, $workingArea.Width)
        $windowHeight = [Math]::Min($windowHeight, $workingArea.Height)

        [void][WindowPlacement]::MoveWindow($windowHandle, $workingArea.Left, $workingArea.Top, $windowWidth, $windowHeight, $true)
        Write-LauncherLog "Moved '$($windowGroup.Label)' to the $targetMonitor monitor"
    }
}

function Invoke-MaximizeWindowTitle {
    param(
        [string]$StepName,
        [string[]]$TitleCandidates,
        [switch]$DryRun
    )

    $uniqueTitles = @($TitleCandidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
    if ($uniqueTitles.Count -eq 0) {
        return
    }

    if ($DryRun) {
        foreach ($candidate in $uniqueTitles) {
            Write-LauncherLog "[DryRun] Would maximize window for '$StepName' using title '$candidate'"
        }
        return
    }

    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WindowMaximize {
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
"@ -ErrorAction SilentlyContinue

    $shell = New-Object -ComObject WScript.Shell
    foreach ($candidate in $uniqueTitles) {
        if (-not $shell.AppActivate($candidate)) {
            continue
        }

        Start-Sleep -Milliseconds 250
        $foregroundWindowHandle = $null
        try { $foregroundWindowHandle = [WindowMaximize]::GetForegroundWindow() } catch { $null = $_ }
        if ($foregroundWindowHandle -and $foregroundWindowHandle -ne [IntPtr]::Zero) {
            [void][WindowMaximize]::ShowWindowAsync($foregroundWindowHandle, 3)
            Write-LauncherLog "Maximized window for '$StepName' using title '$candidate'"
            return
        }
    }

    Write-LauncherLog "Could not find a window to maximize for '$StepName'" -Level "WARN"
}

function Invoke-PreLoginWindowPreparation {
    param(
        [object]$Step,
        [System.Diagnostics.Process]$Process,
        [switch]$DryRun
    )

    Invoke-MoveWindowToMonitor -Step $Step -Process $Process -BeforeLogin -DryRun:$DryRun

    $shouldMaximizeBeforeLogin = $false
    if ($Step.PSObject.Properties.Name -contains "maximizeBeforeLogin") {
        $shouldMaximizeBeforeLogin = [bool]$Step.maximizeBeforeLogin
    }

    if ($shouldMaximizeBeforeLogin) {
        $maximizeTitleCandidates = @()
        if ($Step.PSObject.Properties.Name -contains "maximizeWindowTitles") {
            $maximizeTitleCandidates += @($Step.maximizeWindowTitles | ForEach-Object { [string]$_ })
        }

        if ($maximizeTitleCandidates.Count -eq 0) {
            if ($Step.PSObject.Properties.Name -contains "loginWindowTitle") {
                $maximizeTitleCandidates += [string]$Step.loginWindowTitle
            }
            if ($Step.PSObject.Properties.Name -contains "windowTitle") {
                $maximizeTitleCandidates += [string]$Step.windowTitle
            }
            if ($Step.PSObject.Properties.Name -contains "loginSuccessWindowTitle") {
                $maximizeTitleCandidates += [string]$Step.loginSuccessWindowTitle
            }
            if ($Step.PSObject.Properties.Name -contains "loginFallbackWindowTitles") {
                $maximizeTitleCandidates += @($Step.loginFallbackWindowTitles | ForEach-Object { [string]$_ })
            }
            if ($Step.PSObject.Properties.Name -contains "fallbackWindowTitles") {
                $maximizeTitleCandidates += @($Step.fallbackWindowTitles | ForEach-Object { [string]$_ })
            }
        }

        Invoke-MaximizeWindowTitle -StepName ([string]$Step.name) -TitleCandidates $maximizeTitleCandidates -DryRun:$DryRun
    }

    $preLoginMinimizeTitles = @()
    if ($Step.PSObject.Properties.Name -contains "preLoginMinimizeWindowTitles") {
        $preLoginMinimizeTitles = @($Step.preLoginMinimizeWindowTitles | ForEach-Object { [string]$_ })
    }

    if ($preLoginMinimizeTitles.Count -gt 0) {
        $preLoginMinimizeDelaySeconds = if ($Step.PSObject.Properties.Name -contains "preLoginMinimizeDelaySeconds") {
            [int]$Step.preLoginMinimizeDelaySeconds
        }
        else {
            0
        }

        if ($preLoginMinimizeDelaySeconds -gt 0) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would wait $preLoginMinimizeDelaySeconds second(s) before minimizing pre-login windows for '$($Step.name)'"
            }
            else {
                Start-Sleep -Seconds $preLoginMinimizeDelaySeconds
            }
        }

        Invoke-MinimizeWindowTitle -StepName ([string]$Step.name) -TitleCandidates $preLoginMinimizeTitles -DryRun:$DryRun
    }
}

function Invoke-LaunchStep {
    param(
        [object]$Step,
        [string]$ConfigDirectory,
        [switch]$DryRun
    )

    $rawProgramPath = [string]$Step.programPath
    $looksLikePath = [System.IO.Path]::IsPathRooted($rawProgramPath) -or $rawProgramPath.Contains("/") -or $rawProgramPath.Contains("\\")
    $programPath = if ($looksLikePath) {
        Resolve-StepPath -Path $rawProgramPath -ConfigDirectory $ConfigDirectory
    }
    else {
        $rawProgramPath
    }

    $arguments = if ($Step.arguments) { [string]$Step.arguments } else { "" }
    $windowStyle = if ($Step.PSObject.Properties.Name -contains "windowStyle") { [string]$Step.windowStyle } else { "Normal" }
    $workingDirectory = if ($Step.workingDirectory) {
        Resolve-StepPath -Path ([string]$Step.workingDirectory) -ConfigDirectory $ConfigDirectory
    }
    else {
        if ($looksLikePath -and (Test-Path -Path $programPath)) {
            Split-Path -Path $programPath -Parent
        }
        else {
            $ConfigDirectory
        }
    }

    if ($DryRun) {
        Write-LauncherLog "[DryRun] Would launch: $programPath $arguments (WindowStyle=$windowStyle)"
        Invoke-MinimizeLaunchedWindow -Step $Step -Process $null -DryRun:$DryRun
        Wait-ForWindowToClose -Step $Step -DryRun:$DryRun
        Invoke-PreLoginWindowPreparation -Step $Step -Process $null -DryRun:$DryRun
        if ($Step.PSObject.Properties.Name -contains "loginSequence") {
            Write-LauncherLog "[DryRun] Would run login sequence for '$($Step.name)'"
        }
        if ($Step.PSObject.Properties.Name -contains "waitForLoginCompleteSeconds") {
            $loginCompleteWait = [int]$Step.waitForLoginCompleteSeconds
            if ($loginCompleteWait -gt 0) {
                Write-LauncherLog "[DryRun] Would wait $loginCompleteWait second(s) for login to complete"
            }
        }
        Confirm-LoginCompletion -Step $Step -Process $null -DryRun:$DryRun
        Invoke-MoveWindowToMonitor -Step $Step -Process $null -DryRun:$DryRun
        Invoke-UpdateTableFlow -Step $Step -DryRun:$DryRun
        Invoke-MinimizeLaunchedWindow -Step $Step -Process $null -AfterCompletion -DryRun:$DryRun
        return
    }

    $launchTarget = $null
    $launchTargetIsDirectory = $false
    if ($looksLikePath) {
        if (-not (Test-Path -Path $programPath)) {
            throw "Program not found for step '$($Step.name)': $programPath"
        }

        $launchTarget = (Resolve-Path -Path $programPath).Path
        try {
            $launchTargetItem = Get-Item -LiteralPath $launchTarget -ErrorAction Stop
            $launchTargetIsDirectory = [bool]$launchTargetItem.PSIsContainer
        }
        catch {
            $launchTargetIsDirectory = $false
        }
    }
    else {
        $resolvedCommand = Get-Command -Name $programPath -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($resolvedCommand) {
            $launchTarget = $resolvedCommand.Source
        }
        else {
            # Let Start-Process try shell/app-path resolution (common for Office apps).
            $launchTarget = $programPath
        }
    }

    Write-LauncherLog "Launching '$($Step.name)'"
    $startInfo = @{
        FilePath         = $launchTarget
        WorkingDirectory = $workingDirectory
        PassThru         = $true
        WindowStyle      = $windowStyle
    }

    if (-not [string]::IsNullOrWhiteSpace($arguments)) {
        $startInfo["ArgumentList"] = $arguments
    }

    $process = $null
    if ($launchTargetIsDirectory) {
        Write-LauncherLog "Launch target '$launchTarget' is a directory; opening via shell"
        try {
            $shell = New-Object -ComObject WScript.Shell
            $quotedTarget = '"' + [string]$launchTarget + '"'
            [void]$shell.Run($quotedTarget, 1, $false)
            $process = $null
        }
        catch {
            throw "Failed to launch step '$($Step.name)' with target '$launchTarget'. $($_.Exception.Message)"
        }
    }
    else {
        try {
            $process = Start-Process @startInfo
        }
        catch {
            $launchExtension = [string]([System.IO.Path]::GetExtension([string]$launchTarget)).ToLowerInvariant()
            $useShellFallback = $looksLikePath -and (@('.lnk', '.accdb', '.accde') -contains $launchExtension -or $launchTargetIsDirectory)
            if ($useShellFallback) {
                Write-LauncherLog "Direct launch failed for '$launchTarget'; retrying via shell open" -Level "WARN"
                try {
                    $shell = New-Object -ComObject WScript.Shell
                    $quotedTarget = '"' + [string]$launchTarget + '"'
                    [void]$shell.Run($quotedTarget, 1, $false)
                    $process = $null
                }
                catch {
                    throw "Failed to launch step '$($Step.name)' with target '$launchTarget'. $($_.Exception.Message)"
                }
            }
            else {
                throw "Failed to launch step '$($Step.name)' with target '$launchTarget'. $($_.Exception.Message)"
            }
        }
    }

    $postLaunchDelaySeconds = if ($Step.postLaunchDelaySeconds) { [int]$Step.postLaunchDelaySeconds } else { 3 }
    if ($postLaunchDelaySeconds -gt 0) {
        Start-Sleep -Seconds $postLaunchDelaySeconds
    }

    Invoke-MinimizeLaunchedWindow -Step $Step -Process $process -DryRun:$DryRun

    Wait-ForWindowToClose -Step $Step -DryRun:$DryRun

    Invoke-PreLoginWindowPreparation -Step $Step -Process $process -DryRun:$DryRun
    Send-LoginSequence -Step $Step -Process $process -DryRun:$DryRun
    Invoke-UpdateTableFlow -Step $Step -Process $process -DryRun:$DryRun

    if ($Step.PSObject.Properties.Name -contains "waitForLoginCompleteSeconds") {
        $loginCompleteWait = [int]$Step.waitForLoginCompleteSeconds
        if ($loginCompleteWait -gt 0) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would wait $loginCompleteWait second(s) for login to complete"
            }
            else {
                Write-LauncherLog "Waiting $loginCompleteWait second(s) for login to complete"
                Start-Sleep -Seconds $loginCompleteWait
            }
        }
    }

    Confirm-LoginCompletion -Step $Step -Process $process -DryRun:$DryRun
    Invoke-MoveWindowToMonitor -Step $Step -Process $process -DryRun:$DryRun
    Invoke-MinimizeLaunchedWindow -Step $Step -Process $process -AfterCompletion -DryRun:$DryRun

    if ($Step.PSObject.Properties.Name -contains "minimizeAdditionalWindowTitlesAfterCompletion") {
        $additionalMinimizeDelaySeconds = if ($Step.PSObject.Properties.Name -contains "minimizeAdditionalWindowTitlesAfterCompletionDelaySeconds") {
            [int]$Step.minimizeAdditionalWindowTitlesAfterCompletionDelaySeconds
        }
        else {
            0
        }

        if ($additionalMinimizeDelaySeconds -gt 0) {
            if ($DryRun) {
                Write-LauncherLog "[DryRun] Would wait $additionalMinimizeDelaySeconds second(s) before minimizing additional windows after completion for '$($Step.name)'"
            }
            else {
                Start-Sleep -Seconds $additionalMinimizeDelaySeconds
            }
        }

        $additionalTitles = @($Step.minimizeAdditionalWindowTitlesAfterCompletion | ForEach-Object { [string]$_ })
        Invoke-MinimizeWindowTitle -StepName ([string]$Step.name) -TitleCandidates $additionalTitles -DryRun:$DryRun
    }
}

$configFile = $null
$configDirectory = $null
$completedSuccessfully = $false

try {
    $configFile = Resolve-Path -Path $ConfigPath -ErrorAction Stop
    $configDirectory = Split-Path -Path $configFile -Parent

    Show-LauncherBanner -IsDryRun:$DryRun
    Write-LauncherLog "Launcher version $($script:LauncherVersion)"
    Update-DesktopLauncherLink -ScriptDirectory $PSScriptRoot
    Write-LauncherLog "Loading configuration from $configFile"
    $configRaw = Get-Content -Path $configFile -Raw
    $config = $configRaw | ConvertFrom-Json

    $checkForUpdates = $true
    if ($config.PSObject.Properties.Name -contains "checkForUpdates") {
        $checkForUpdates = [bool]$config.checkForUpdates
    }

    $updateCheckUrl = "https://raw.githubusercontent.com/Irish-Coder69/Launcher/main/update/versions.json"
    if ($config.PSObject.Properties.Name -contains "updateCheckUrl") {
        $configuredUpdateCheckUrl = [string]$config.updateCheckUrl
        if (-not [string]::IsNullOrWhiteSpace($configuredUpdateCheckUrl)) {
            $updateCheckUrl = $configuredUpdateCheckUrl
        }
    }

    $requireUpdateBeforeRun = $false
    if ($config.PSObject.Properties.Name -contains "requireUpdateBeforeRun") {
        $requireUpdateBeforeRun = [bool]$config.requireUpdateBeforeRun
    }

    if ($checkForUpdates -and -not $DryRun -and (Get-Command Invoke-UpdateCheck -ErrorAction SilentlyContinue)) {
        try {
            Write-LauncherLog "Checking for Launcher updates from GitHub..."
            if ($requireUpdateBeforeRun) {
                Write-LauncherLog "Update-before-run is enabled; launcher will install updates before continuing when a newer version is found."
                $updateResult = Invoke-UpdateCheck `
                    -InstallDir $PSScriptRoot `
                    -VersionsUrl $updateCheckUrl `
                    -Block:$true `
                    -Silent:$true `
                    -InstallBeforeContinue:$true `
                    -RequireAnyUpdate:$true

                if ($updateResult -and $updateResult.UpdateInstalled) {
                    Write-LauncherLog "Update installed to version $($updateResult.Latest). Restart Launcher to run the latest build."
                    Write-Host ""
                    Write-Host (("=" * $script:UIWidth)) -ForegroundColor DarkCyan
                    Write-Host "  Launcher updated successfully. Restarting now will use the latest version." -ForegroundColor Green
                    Write-Host (("=" * $script:UIWidth)) -ForegroundColor DarkCyan
                    $completedSuccessfully = $true
                    return
                }
            }
            else {
                Invoke-UpdateCheck `
                    -InstallDir $PSScriptRoot `
                    -VersionsUrl $updateCheckUrl `
                    -Block:$false `
                    -Silent:$true | Out-Null
            }
        }
        catch {
            Write-LauncherLog "Update check could not be started: $($_.Exception.Message)" -Level "WARN"
        }
    }

    if (-not $config.steps -or $config.steps.Count -eq 0) {
        throw "No steps were found in $configFile"
    }

    foreach ($step in $config.steps) {
        if (-not $step.enabled) {
            Write-LauncherLog "Skipping disabled step '$($step.name)'"
            continue
        }

        Write-StepHeader -Name $step.name

        try {
            switch ([string]$step.type) {
                "launch" {
                    Invoke-LaunchStep -Step $step -ConfigDirectory $configDirectory -DryRun:$DryRun
                    break
                }
                "access-sql" {
                    Invoke-AccessSqlStep -Step $step -ConfigDirectory $configDirectory -DryRun:$DryRun
                    break
                }
                default {
                    throw "Unsupported step type '$($step.type)' in step '$($step.name)'"
                }
            }

            if ($step.waitAfterStepSeconds -and [int]$step.waitAfterStepSeconds -gt 0) {
                Start-Sleep -Seconds ([int]$step.waitAfterStepSeconds)
            }
        }
        catch {
            Write-LauncherLog "Step '$($step.name)' failed: $($_.Exception.Message)" -Level "ERROR"
            throw
        }
    }

    $ensureCapsLockOn = $false
    if ($config.PSObject.Properties.Name -contains "ensureCapsLockOn") {
        $ensureCapsLockOn = [bool]$config.ensureCapsLockOn
    }

    $ensureNumLockOn = $false
    if ($config.PSObject.Properties.Name -contains "ensureNumLockOn") {
        $ensureNumLockOn = [bool]$config.ensureNumLockOn
    }

    Set-LockKeysOn -EnsureCapsLockOn:$ensureCapsLockOn -EnsureNumLockOn:$ensureNumLockOn -DryRun:$DryRun

    Write-LauncherLog "Launcher sequence completed."

    Write-Host ""
    Write-Host (("=" * $script:UIWidth)) -ForegroundColor DarkCyan
    Write-Host "  All steps completed." -ForegroundColor Green
    Write-Host (("=" * $script:UIWidth)) -ForegroundColor DarkCyan

    $completedSuccessfully = $true
}
finally {
    if (-not $DryRun -and -not $completedSuccessfully) {
        Write-Host ""
        Write-Host (("=" * $script:UIWidth)) -ForegroundColor DarkCyan
        Write-Host "  Launcher stopped due to an error." -ForegroundColor Red
        Write-Host (("=" * $script:UIWidth)) -ForegroundColor DarkCyan
    }

    Wait-ForLauncherCloseCommand -DryRun:$DryRun
}
