param([Parameter(Mandatory)][string] $AppDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$profile = Join-Path $env:RUNNER_TEMP ('dmct-profile-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $profile | Out-Null
$originalAppData = $env:LOCALAPPDATA
$env:LOCALAPPDATA = $profile
$app = $null
try {
    $app = Start-Process -FilePath (Join-Path $AppDirectory 'DMCommandTable.exe') -WorkingDirectory $AppDirectory -PassThru
    $register = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty, 'Register'
    )
    $deadline = [DateTime]::UtcNow.AddSeconds(65)
    do {
        if ($app.HasExited) { throw "WinUI exited before first-run registration ($($app.ExitCode))" }
        $app.Refresh()
        if ($app.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($app.MainWindowHandle)
            if ($window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $register)) {
                Write-Output 'WinUI started the bundled engine and displayed first-run registration.'
                if (!$app.CloseMainWindow() -or !$app.WaitForExit(12000)) {
                    throw 'WinUI did not close cleanly.'
                }
                return
            }
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The WinUI first-run registration screen did not appear.'
} catch {
    $startupLog = Join-Path $profile 'DM Command Table/winui-startup-error.log'
    if (Test-Path -LiteralPath $startupLog) { Get-Content -LiteralPath $startupLog -TotalCount 6 | Write-Output }
    Get-ChildItem -LiteralPath $AppDirectory -Filter '*.pri' | ForEach-Object { Write-Output "Packaged resource index: $($_.Name)" }
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = [DateTime]::Now.AddMinutes(-2); Id = @(1000, 1026) } -ErrorAction SilentlyContinue |
        Where-Object { $_.Message -match 'DMCommandTable' } |
        Select-Object -First 1 -ExpandProperty Message | Write-Output
    throw
} finally {
    if ($app -and !$app.HasExited) {
        & taskkill.exe /pid $app.Id /t /f | Out-Null
    }
    $env:LOCALAPPDATA = $originalAppData
    Remove-Item -LiteralPath $profile -Recurse -Force
}
