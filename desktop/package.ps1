param(
    [Parameter(Mandatory)]
    [string] $EngineZip,
    [string] $Output = (Join-Path $PSScriptRoot '..\dist\DMCommandTable-WinUI')
)
$ErrorActionPreference = 'Stop'
$EngineZip = (Resolve-Path -LiteralPath $EngineZip).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Output already exists: $Output" }
$stage = Join-Path ([IO.Path]::GetTempPath()) ("dmct-" + [Guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $EngineZip -DestinationPath $stage
    $engine = Join-Path $stage 'DMCT'
    if (!(Test-Path -LiteralPath (Join-Path $engine 'node.exe'))) { throw 'Missing bundled Node engine' }
    $project = Join-Path $PSScriptRoot 'DMCommandTable\DMCommandTable.csproj'
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\amd64\MSBuild.exe' | Select-Object -First 1
    if (!$msbuild) { throw 'Install Visual Studio Build Tools with Windows application development tools.' }
    $buildLog = Join-Path $stage 'winui-build.log'
    $buildLayout = Join-Path $stage 'winui'
    Push-Location $PSScriptRoot
    try {
        & $msbuild $project -restore -t:Publish -p:Configuration=Release -p:Platform=x64 `
            -p:RuntimeIdentifier=win-x64 -p:SelfContained=true "-p:OutputPath=$buildLayout" "-p:PublishDir=$Output" -verbosity:minimal 2>&1 |
            Tee-Object -FilePath $buildLog
    } finally {
        Pop-Location
    }
    if ($LASTEXITCODE -ne 0) {
        if ($env:GITHUB_ACTIONS -eq 'true') {
            Get-Content -LiteralPath $buildLog | Where-Object { $_ -match '(error |failed)' } |
                Select-Object -Unique -First 10 | ForEach-Object {
                    Write-Output "::error::$($_.Replace('%', '%25'))"
                }
        }
        throw "WinUI build failed with exit code $LASTEXITCODE"
    }
    # The desktop runtime uses loose files as well as assemblies and resource
    # indexes. Keep the complete build layout when Publish omits these files.
    $builtApp = Get-ChildItem -LiteralPath $buildLayout -Filter 'DMCommandTable.exe' -Recurse |
        Where-Object { $_.DirectoryName -notlike '*\publish' } | Select-Object -First 1
    if ($builtApp) {
        Get-ChildItem -LiteralPath $builtApp.DirectoryName -Force |
            Where-Object { $_.Name -ne 'publish' } |
            ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $Output -Recurse -Force }
    }
    Move-Item -LiteralPath $engine -Destination (Join-Path $Output 'engine')
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
