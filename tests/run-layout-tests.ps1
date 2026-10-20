param([string]$SourceDirectory, [string]$OutputDirectory, [switch]$SkipClipboard, [switch]$ClipboardOnly)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
if (-not $SourceDirectory) { $SourceDirectory = Join-Path $projectDirectory 'src' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectDirectory 'work\dpi-tests\current' }
$SourceDirectory = [System.IO.Path]::GetFullPath($SourceDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExecutable = Join-Path $OutputDirectory 'LayoutRegressionTests.exe'
$references = @('/reference:System.Web.Extensions.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Net.Http.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')
$sources = @((Join-Path $PSScriptRoot 'LayoutRegressionTests.cs'), (Join-Path $SourceDirectory 'MainForm.cs'),
    (Join-Path $SourceDirectory 'DownloadConfirmForm.cs'), (Join-Path $SourceDirectory 'DownloadService.cs'),
    (Join-Path $SourceDirectory 'ToolInstaller.cs'))
$assemblyInfo = Join-Path $SourceDirectory 'AssemblyInfo.cs'
if (Test-Path -LiteralPath $assemblyInfo) { $sources += $assemblyInfo }
$manifest = Join-Path $SourceDirectory 'app.manifest'
$compilerOptions = @('/nologo', '/target:exe')
if (Test-Path -LiteralPath $manifest) { $compilerOptions += "/win32manifest:$manifest" }
& $compiler @compilerOptions @references "/out:$testExecutable" @sources
if ($LASTEXITCODE -ne 0) { throw 'Layout regression test compilation failed.' }
$appConfig = Join-Path $SourceDirectory 'app.config'
$testArguments = @($OutputDirectory)
if (Test-Path -LiteralPath $appConfig) { Copy-Item -LiteralPath $appConfig -Destination "$testExecutable.config" -Force }
else { $testArguments += '--legacy-system-dpi' }
if ($SkipClipboard) { $testArguments += '--skip-clipboard' }
if ($ClipboardOnly) { $testArguments += '--clipboard-only' }
& $testExecutable @testArguments 2>&1 | Tee-Object -FilePath (Join-Path $OutputDirectory 'layout-tests.log')
if ($LASTEXITCODE -ne 0) { throw "Layout regression tests failed. See $OutputDirectory\layout-tests.log" }
