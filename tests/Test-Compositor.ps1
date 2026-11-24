$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'obj\native-x64'
& (Join-Path $root 'native\BuildNative.cmd') $build
if ($LASTEXITCODE -ne 0) { throw 'Native build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$test = Join-Path $build 'CompositorTest.exe'
& $compiler /nologo /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$test" (Join-Path $PSScriptRoot 'CompositorTest.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $test
if ($LASTEXITCODE -ne 0) { throw 'Compositor test failed' }
