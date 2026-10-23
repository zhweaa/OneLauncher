$ErrorActionPreference = 'Stop'
$instancePath = 'C:/Users/wwwin/OneLauncher/installed/minecraft/instance/6a710be5'
$launchArgsPath = 'C:/Users/wwwin/OneLauncher/LastLaunchArgs.bat'
$launchArgsText = [IO.File]::ReadAllText($launchArgsPath)
$gameDirectoryMatch = [regex]::Match($launchArgsText, '--gameDir\s+"([^"]+)"')
if ([IO.Path]::GetFullPath($gameDirectoryMatch.Groups[1].Value) -ne [IO.Path]::GetFullPath($instancePath)) {
    throw 'The saved launch command does not target the specified modpack instance.'
}
$processInfo = [Diagnostics.ProcessStartInfo]::new()
$processInfo.FileName = 'C:/Program Files/Eclipse Adoptium/jdk-21.0.7.6-hotspot/bin/java.exe'
$processInfo.Arguments = $launchArgsText
$processInfo.WorkingDirectory = $instancePath
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true
$processInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$gameProcess = [Diagnostics.Process]::new()
$gameProcess.StartInfo = $processInfo
$timer = [Diagnostics.Stopwatch]::StartNew()
$stoppedAfterObservation = $false
try {
    [void]$gameProcess.Start()
    $stdoutTask = $gameProcess.StandardOutput.ReadToEndAsync()
    $stderrTask = $gameProcess.StandardError.ReadToEndAsync()
    while (!$gameProcess.HasExited -and $timer.Elapsed.TotalSeconds -lt 180) {
        [Threading.Thread]::Sleep(500)
    }
    if (!$gameProcess.HasExited) {
        $stoppedAfterObservation = $true
        $gameProcess.Kill($true)
    }
    $gameProcess.WaitForExit()
    $outputText = $stdoutTask.GetAwaiter().GetResult() + [Environment]::NewLine + $stderrTask.GetAwaiter().GetResult()
    $accessTokenMatch = [regex]::Match($launchArgsText, '--accessToken\s+"([^"]+)"')
    if ($accessTokenMatch.Success) { $outputText = $outputText.Replace($accessTokenMatch.Groups[1].Value, '[redacted]') }
    $logPath = Join-Path $PSScriptRoot 'real-modpack-launch.log'
    [IO.File]::WriteAllText($logPath, $outputText)
    [pscustomobject]@{
        ExitCode = $gameProcess.ExitCode
        Seconds = [Math]::Round($timer.Elapsed.TotalSeconds, 1)
        StoppedAfterObservation = $stoppedAfterObservation
        WorkingDirectory = $instancePath
        FancyMenuPathError = ($outputText -match 'ScreenCustomization:readCustomizableScreensFromFile.*IOException')
        FancyMenuNullError = ($outputText -match 'customizableScreens.*null')
        CrashReported = ($outputText -match 'Crash report saved to:')
        ResourceReloadStarted = ($outputText -match 'Reloading ResourceManager')
        OpenALInitialized = ($outputText -match 'OpenAL initialized')
        LogPath = $logPath
    } | ConvertTo-Json -Compress
}
finally {
    if ($gameProcess.Id -and !$gameProcess.HasExited) { $gameProcess.Kill($true) }
    $gameProcess.Dispose()
}
