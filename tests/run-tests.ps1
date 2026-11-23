$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sourceExecutable = Join-Path $repo 'dist\Antigravity-Proxy-Launcher.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable)) { & (Join-Path $repo 'build.ps1') }
$fixture = Join-Path $repo ('.test-output\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
& $compiler /nologo "/out:$fixture\ShortcutProbe.exe" "$PSScriptRoot\EnvironmentProbe.cs"
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed.' }
& $compiler /nologo /utf8output /target:exe /main:SmokeTests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll "/win32manifest:$repo\assets\app.manifest" "/out:$fixture\SmokeTests.exe" "$repo\src\Launcher.cs" "$PSScriptRoot\SmokeTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Smoke test compilation failed.' }
& (Join-Path $fixture 'SmokeTests.exe') $fixture $sourceExecutable
if ($LASTEXITCODE -ne 0) { throw 'Smoke tests failed.' }
Write-Output 'All smoke tests passed. No real desktop or Start menu entries were changed.'