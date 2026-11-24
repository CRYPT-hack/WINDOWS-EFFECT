param([switch]$LiveDesktop)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
& (Join-Path $root 'Build.ps1')
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.Xaml.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { "/reference:$(Join-Path $framework $_)" }
$bin=Join-Path $root 'bin'
$program=Join-Path $bin 'WindowWeather.SnowTests.exe'
& (Join-Path $framework csc.exe) /nologo /platform:x64 "/out:$program" "/reference:$bin\Window Weather.exe" "/win32manifest:$root\app.manifest" @refs (Join-Path $PSScriptRoot 'SnowTests.cs')
if($LASTEXITCODE -ne 0){throw 'Snow test compilation failed'}
if($LiveDesktop){ & $program --live }else{ & $program }
if($LASTEXITCODE -ne 0){throw 'Snow tests failed'}
