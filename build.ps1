param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler not found. .NET Framework 4.8 is required.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = Join-Path $OutputDirectory 'Antigravity-Proxy-Launcher.exe'
& $compiler /nologo /target:winexe /optimize+ /platform:anycpu /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll "/win32manifest:$PSScriptRoot\assets\app.manifest" "/win32icon:$PSScriptRoot\assets\app.ico" "/out:$output" "$PSScriptRoot\src\Launcher.cs" "$PSScriptRoot\src\Features.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output "Built $output"
