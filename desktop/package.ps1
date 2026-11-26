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
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $Output
    if ($LASTEXITCODE -ne 0) { throw "WinUI build failed with exit code $LASTEXITCODE" }
    Move-Item -LiteralPath $engine -Destination (Join-Path $Output 'engine')
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}
