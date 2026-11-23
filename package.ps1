param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$dist = Join-Path $PSScriptRoot 'dist'
$exe = Join-Path $dist 'Antigravity-Proxy-Launcher.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Run build.ps1 before packaging.' }
$version = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion -replace '\.0$',''
$archive = Join-Path $dist "Antigravity-Proxy-Launcher-v$version-windows.zip"
Compress-Archive -LiteralPath $exe,(Join-Path $PSScriptRoot 'LICENSE'),(Join-Path $PSScriptRoot 'docs\usage.md') -DestinationPath $archive -Force
$checksums = foreach ($file in @($exe,$archive)) { '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(),[IO.Path]::GetFileName($file) }
[IO.File]::WriteAllLines((Join-Path $dist 'SHA256SUMS.txt'), $checksums, [Text.Encoding]::ASCII)
Write-Output "Packaged $archive"
