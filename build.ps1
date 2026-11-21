param(
    [Parameter(Position=0)][string]$ManagedDir = $env:ONI_MANAGED_DIR,
    [Parameter(Position=1)][string]$OutputDir = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
if ([string]::IsNullOrWhiteSpace($ManagedDir) -or
    -not (Test-Path -LiteralPath (Join-Path $ManagedDir 'Assembly-CSharp.dll') -PathType Leaf)) {
    throw 'Specify the ONI Managed folder containing Assembly-CSharp.dll (-ManagedDir or ONI_MANAGED_DIR).'
}
$managed = (Resolve-Path -LiteralPath $ManagedDir).ProviderPath
$null = Get-Command $dotnet
$null = New-Item -ItemType Directory -Force -Path $OutputDir
$out = (Resolve-Path -LiteralPath $OutputDir).ProviderPath
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('oni-precision-' + [System.Guid]::NewGuid().ToString('N'))
$stage = Join-Path $tmp 'package'
$mod = Join-Path $stage 'OniPrecision'
$bin = Join-Path $tmp 'build'
try {
    $null = New-Item -ItemType Directory -Path $mod -Force
    & $dotnet build (Join-Path $PSScriptRoot 'OniPrecision.csproj') -c Release -o $bin "-p:OniManagedDir=$managed"
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)." }
    foreach ($name in @('mod.yaml', 'mod_info.yaml')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $mod
    }
    Copy-Item -LiteralPath (Join-Path $bin 'OniPrecision.dll') -Destination $mod
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.zh-CN.md') -Destination $stage
    $archive = Join-Path $tmp 'OniPrecision-install.zip'
    Compress-Archive -LiteralPath @($mod, (Join-Path $stage 'README.zh-CN.md')) -DestinationPath $archive
    $result = Join-Path $out 'OniPrecision-install.zip'
    Copy-Item -LiteralPath $archive -Destination $result -Force
    Write-Output "Install archive: $result"
} finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force }
}
