param([string]$DestinationDirectory)
$ErrorActionPreference = 'Stop'
$projectDirectory = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'This build requires Windows x64 and .NET Framework 4.8.' }
$outputDirectory = if ($DestinationDirectory) { [System.IO.Path]::GetFullPath($DestinationDirectory) } else { Join-Path $projectDirectory 'release' }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectDirectory 'src') -Filter '*.cs' | ForEach-Object FullName
$compilerArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output',
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll', '/reference:System.Net.Http.dll',
    '/reference:System.Web.Extensions.dll', '/reference:System.IO.Compression.dll',
    '/reference:System.IO.Compression.FileSystem.dll',
    ('/win32manifest:' + (Join-Path $projectDirectory 'src\app.manifest')),
    ('/win32icon:' + (Join-Path $projectDirectory 'src\app.ico')),
    ('/out:' + (Join-Path $outputDirectory '影片與音樂下載器.exe')))
& $compiler @compilerArguments @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectDirectory 'src\app.config') -Destination (Join-Path $outputDirectory '影片與音樂下載器.exe.config')
Copy-Item -LiteralPath (Join-Path $projectDirectory 'README.md') -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $projectDirectory 'THIRD_PARTY_NOTICES.md') -Destination $outputDirectory
Copy-Item -LiteralPath (Join-Path $projectDirectory 'LICENSE') -Destination $outputDirectory
Write-Output (Join-Path $outputDirectory '影片與音樂下載器.exe')
