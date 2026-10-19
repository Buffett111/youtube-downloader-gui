$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $projectDirectory 'work\behavior-tests'
$fixtureDirectory = Join-Path $testDirectory '工具 tools'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $testDirectory, $fixtureDirectory | Out-Null

$mockExecutable = Join-Path $fixtureDirectory 'yt-dlp.exe'
& $compiler /nologo /target:exe /reference:System.Web.Extensions.dll "/out:$mockExecutable" (Join-Path $PSScriptRoot 'MockDownloader.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mock downloader compilation failed.' }
Copy-Item -LiteralPath $mockExecutable -Destination (Join-Path $fixtureDirectory 'ffmpeg.exe') -Force
Copy-Item -LiteralPath $mockExecutable -Destination (Join-Path $fixtureDirectory 'ffprobe.exe') -Force
Copy-Item -LiteralPath $mockExecutable -Destination (Join-Path $fixtureDirectory 'deno.exe') -Force

$testExecutable = Join-Path $testDirectory 'BehaviorTests.exe'
& $compiler /nologo /target:exe /reference:System.Web.Extensions.dll "/out:$testExecutable" (Join-Path $PSScriptRoot 'BehaviorTests.cs') (Join-Path $projectDirectory 'src\DownloadService.cs')
if ($LASTEXITCODE -ne 0) { throw 'Behavior test compilation failed.' }
& $testExecutable $fixtureDirectory
if ($LASTEXITCODE -ne 0) { throw 'Behavior tests failed.' }

$guiTestExecutable = Join-Path $testDirectory 'GuiConfirmationTests.exe'
$guiReferences = @('/reference:System.Web.Extensions.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Net.Http.dll', '/reference:System.IO.Compression.dll', '/reference:System.IO.Compression.FileSystem.dll')
$guiSources = @((Join-Path $PSScriptRoot 'GuiConfirmationTests.cs'), (Join-Path $projectDirectory 'src\MainForm.cs'),
    (Join-Path $projectDirectory 'src\DownloadConfirmForm.cs'), (Join-Path $projectDirectory 'src\DownloadService.cs'),
    (Join-Path $projectDirectory 'src\ToolInstaller.cs'))
& $compiler /nologo /target:exe @guiReferences "/out:$guiTestExecutable" @guiSources
if ($LASTEXITCODE -ne 0) { throw 'GUI confirmation test compilation failed.' }
& $guiTestExecutable $fixtureDirectory
if ($LASTEXITCODE -ne 0) { throw 'GUI confirmation tests failed.' }
