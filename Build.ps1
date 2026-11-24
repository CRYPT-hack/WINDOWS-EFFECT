param([string]$OutputPath = (Join-Path $PSScriptRoot 'bin\Window Weather.exe'))
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$output = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
$nativeBuild = Join-Path $PSScriptRoot 'obj\native-x64'
& (Join-Path $PSScriptRoot 'native\BuildNative.cmd') $nativeBuild
if ($LASTEXITCODE -ne 0) { throw 'GPU renderer build failed' }
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll',
    'System.Xaml.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll',
    'WPF\PresentationCore.dll','WPF\PresentationFramework.dll','WPF\WindowsBase.dll')
$flags = @('/nologo','/target:winexe','/platform:anycpu','/optimize+',"/out:$output",
    "/resource:$nativeBuild\WindowWeather.Gpu.dll,WindowWeather.Gpu.dll",
    "/win32manifest:$PSScriptRoot\app.manifest","/win32icon:$PSScriptRoot\Weather.ico",
    "/resource:$PSScriptRoot\Controls.xaml,Controls.xaml","/resource:$PSScriptRoot\Weather.ico,Weather.ico")
foreach ($ref in $refs) { $flags += "/reference:$(Join-Path $framework $ref)" }
$files = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
& (Join-Path $framework 'csc.exe') @flags @files
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Write-Output $output