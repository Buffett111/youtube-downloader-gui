param([string]$ToolsDirectory, [switch]$Mp4Only)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
if (-not $ToolsDirectory) { $ToolsDirectory = Join-Path $projectDirectory 'work\installer-smoke-tools' }
$ToolsDirectory = [System.IO.Path]::GetFullPath($ToolsDirectory)
$runDirectory = Join-Path $projectDirectory ('work\local-media-integration\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExecutable = Join-Path $runDirectory 'LocalMediaIntegration.exe'
& $compiler /nologo /target:exe /reference:System.Web.Extensions.dll "/out:$testExecutable" (Join-Path $PSScriptRoot 'LocalMediaIntegration.cs') (Join-Path $projectDirectory 'src\DownloadService.cs')
if ($LASTEXITCODE -ne 0) { throw 'Local integration test compilation failed.' }
$testArguments = @($ToolsDirectory, $runDirectory)
if ($Mp4Only) { $testArguments += '--mp4-only' }
& $testExecutable @testArguments 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory 'integration.log')
if ($LASTEXITCODE -ne 0) { throw "Local integration tests failed. See $runDirectory\integration.log" }
