param([Parameter(Mandatory)][string] $AppDirectory)
$ErrorActionPreference = 'Stop'
$profile = Join-Path $env:RUNNER_TEMP ('dmct-profile-' + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $profile | Out-Null
$originalAppData = $env:LOCALAPPDATA
$env:LOCALAPPDATA = $profile
$readyFile = Join-Path $profile 'ready.txt'
$env:DM_COMMAND_TABLE_TEST_READY_FILE = $readyFile
$app = $null
try {
    $app = Start-Process -FilePath (Join-Path $AppDirectory 'DMCommandTable.exe') -WorkingDirectory $AppDirectory -PassThru
    if (!$app.WaitForExit(65000)) { throw 'The WinUI first-run launch timed out.' }
    if (Test-Path -LiteralPath $readyFile) {
        $result = (Get-Content -LiteralPath $readyFile -Raw).Trim()
        if ($result -eq 'registration-and-combat-ready' -and $app.ExitCode -eq 0) {
            Write-Output 'WinUI activated its native window and displayed registration and laid out the themed Combat screen.'
            return
        }
        Get-Content -LiteralPath $readyFile -TotalCount 6 | Write-Output
    }
    throw "WinUI did not complete first-run registration (exit code $($app.ExitCode))."
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
    Remove-Item Env:DM_COMMAND_TABLE_TEST_READY_FILE -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $profile -Recurse -Force
}
