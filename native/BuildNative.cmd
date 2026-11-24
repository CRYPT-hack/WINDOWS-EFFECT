@echo off
setlocal
set "build=%~1"
set "src=%~dp0"
if not exist "%build%" mkdir "%build%" || exit /b 1
for /f "usebackq delims=" %%p in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "vs=%%p"
if not defined vs (
  echo Install Visual Studio C++ Build Tools and the Windows SDK to build the GPU renderer.
  exit /b 1
)
call "%vs%\Common7\Tools\VsDevCmd.bat" -arch=x64 -host_arch=x64 -no_logo >nul || exit /b 1
pushd "%build%" || exit /b 1
fxc /nologo /O3 /T vs_5_0 /E Vertex /Fh Vertex.h /Vn g_Vertex "%src%weather.hlsl" || exit /b 1
fxc /nologo /O3 /T ps_5_0 /E Pixel /Fh Pixel.h /Vn g_Pixel "%src%weather.hlsl" || exit /b 1
cl /nologo /O2 /MT /EHsc /LD /I. "%src%Renderer.cpp" "%src%DesktopIcons.cpp" /link /OUT:WindowWeather.Gpu.dll user32.lib d3d11.lib dxgi.lib dcomp.lib ole32.lib shell32.lib uuid.lib || exit /b 1
popd
