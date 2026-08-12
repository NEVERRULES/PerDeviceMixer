[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ExecutablePath,

    [ValidateRange(5, 600)]
    [int]$IdleSeconds = 60,

    [switch]$OpenThenClose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$processName = [IO.Path]::GetFileNameWithoutExtension($resolvedExecutable)
$existingProcesses = @([Diagnostics.Process]::GetProcessesByName($processName) |
    Where-Object {
        try {
            [string]::Equals($_.MainModule.FileName, $resolvedExecutable, [StringComparison]::OrdinalIgnoreCase)
        }
        catch { $false }
    })
if ($existingProcesses.Count -gt 0) {
    $existingProcesses | ForEach-Object { $_.Dispose() }
    throw "An instance from the same executable path is already running."
}
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$diagnosticDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot (
    "PerDeviceMixer.Memory-" + [Guid]::NewGuid().ToString("N"))))
if (-not $diagnosticDirectory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The diagnostic profile path escaped the Windows temporary directory."
}

[IO.Directory]::CreateDirectory($diagnosticDirectory) | Out-Null
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedExecutable
$startInfo.Arguments = if ($OpenThenClose.IsPresent) { "" } else { "--minimized" }
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Environment["PERDEVICEMIXER_DATA_DIRECTORY"] = $diagnosticDirectory
$launcher = [Diagnostics.Process]::Start($startInfo)
if ($null -eq $launcher) { throw "PerDeviceMixer did not start." }
$process = $launcher

try {
    if ($OpenThenClose.IsPresent) {
        Start-Sleep -Seconds 3
    }

    if ($process.HasExited) {
        $process.Dispose()
        $process = $null
        $startupDeadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process = [Diagnostics.Process]::GetProcessesByName($processName) |
                Where-Object {
                    try {
                        [string]::Equals($_.MainModule.FileName, $resolvedExecutable, [StringComparison]::OrdinalIgnoreCase)
                    }
                    catch { $false }
                } |
                Sort-Object StartTime -Descending |
                Select-Object -First 1
            if ($null -eq $process) { Start-Sleep -Milliseconds 250 }
        } while ($null -eq $process -and [DateTime]::UtcNow -lt $startupDeadline)
    }
    if ($null -eq $process) {
        throw "PerDeviceMixer exited before the memory sample was collected."
    }

    if ($OpenThenClose.IsPresent) {
        $windowDeadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.MainWindowHandle -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100 }
        } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $windowDeadline)
        if ($process.MainWindowHandle -eq [IntPtr]::Zero -or -not $process.CloseMainWindow()) {
            throw "PerDeviceMixer did not expose a closable main window."
        }

        $closeDeadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 100
            if ($process.HasExited) { break }
            $process.Refresh()
        } while ($process.MainWindowHandle -ne [IntPtr]::Zero -and [DateTime]::UtcNow -lt $closeDeadline)
        if ($process.HasExited) {
            throw "PerDeviceMixer exited instead of closing to the tray."
        }
    }

    Start-Sleep -Seconds $IdleSeconds

    $process.Refresh()
    [pscustomobject]@{
        Executable = $resolvedExecutable
        Mode = if ($OpenThenClose.IsPresent) { "OpenedThenClosedToTray" } else { "TrayStartup" }
        IdleSeconds = $IdleSeconds
        WorkingSetMB = [Math]::Round($process.WorkingSet64 / 1MB, 1)
        PrivateMemoryMB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 1)
        Threads = $process.Threads.Count
        Handles = $process.HandleCount
    }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    if ($null -ne $process) { $process.Dispose() }

    [Diagnostics.Process]::GetProcessesByName($processName) |
        Where-Object {
            try {
                [string]::Equals($_.MainModule.FileName, $resolvedExecutable, [StringComparison]::OrdinalIgnoreCase)
            }
            catch { $false }
        } |
        ForEach-Object {
            try { $_.Kill(); $_.WaitForExit() } finally { $_.Dispose() }
        }

    $resolvedDiagnosticDirectory = [IO.Path]::GetFullPath($diagnosticDirectory)
    $diagnosticLeaf = [IO.Path]::GetFileName($resolvedDiagnosticDirectory)
    if ($resolvedDiagnosticDirectory.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $diagnosticLeaf.StartsWith("PerDeviceMixer.Memory-", [StringComparison]::Ordinal)) {
        [IO.Directory]::Delete($resolvedDiagnosticDirectory, $true)
    }
}
