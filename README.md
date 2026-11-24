# Window Weather for Windows

Window Weather puts ambient weather among your desktop windows. Rain pools on exposed window edges and runs down their sides. Snow builds up on window tops, desktop icons, and the taskbar. Sweep the pointer through a bank to push powder into a ridge or spill it over the edge. The overlay lets you click, drag, scroll, and type in the application underneath.

This is an independent Windows implementation inspired by Matt Senter’s [Weatherling](https://www.weatherling.app/). The Windows code, artwork, sound synthesis, and birds were created for this project. The official [Mac release notes](https://www.weatherling.app/release-notes) describe the original app, including its Lings, Game Center, and iCloud features. This implementation uses local saved scenes and its own optional bird visitors.

## Run

Run **Window Weather.exe** on Windows 10 or 11 with .NET Framework 4.8 or later. The first launch starts the last saved scene. Open it again to bring up the controls.

- Left-click the green cloud in the notification area to open controls. Windows may put the cloud under the hidden-icons arrow.
- Right-click the cloud for a scene, pause, or quit. **Ctrl + Alt + W** pauses or resumes weather when the shortcut is available.
- Closing the controls leaves the weather running. **Quit** shuts down the app.
- Choose Rain, Snow, Sleet, Leaves, Sunlight, Fog, Blizzard, or Fireflies. Adjust intensity, wind, and the proportion of particles that fall behind windows. Grass and synthesized rain audio are optional.
- Under **Preferences**, choose displays, allow weather in full-screen apps, save or load a named scene, or enable window birds. Hover over a perched bird to greet it. Its count is saved locally.
- Enter a city and choose **Sync city** to follow its weather through [Open-Meteo](https://open-meteo.com/). The app sends the search to Open-Meteo’s geocoder and uses the returned coordinates for forecasts. It refreshes every 30 minutes. Changing the scene, intensity, or wind returns to manual control. Clear nights and overcast skies use the mist scene.
- Full-screen apps pause effects on their display by default. Locking Windows or suspending the laptop pauses animation and audio.

## Snow and seasonal animations

- **Snow** forms a continuous bank. Incoming flakes land on the existing surface. Loose powder spreads down steep slopes, compacts, and melts when the weather warms. A moving ledge can shed clumps onto whatever is below. Larger impacts kick up small puffs of powder.
- **Blizzard** adds denser snowfall, gusting crosswinds, and loose snow blowing off ledges. The Wind control sets the prevailing direction.
- Desktop icon caps sit behind open windows. On x64 Windows, a background worker asks Explorer for icon positions and sizes while snow is active. The taskbar catches flakes along its exposed edge. Snow can reach the bottom of a display when its taskbar is hidden.
- **Leaves** respond to pointer swipes, **Fireflies** drift across the screen and pulse independently, and visiting birds occasionally hop on their perch.

## Build from source

Install Visual Studio or Visual Studio Build Tools with **Desktop development with C++**, the x64 MSVC tools, and a Windows SDK that includes the DirectX shader compiler (`fxc.exe`). From the repository root, run the following command in PowerShell.

```powershell
.\Build.ps1
```

The build produces `bin\Window Weather.exe`. It compiles the shaders and x64 native renderer, then embeds them with the controls and icon in the executable. Use `-OutputPath` to choose a different destination. The C# portion uses the .NET Framework compiler that ships with Windows.

## Rendering

On supported x64 PCs, Direct3D 11 draws each display’s particles, edge deposits, and atmospheric effects in one instanced draw call. DirectComposition blends the transparent surface into the desktop. Window movement and stacking notifications update collision bounds and visibility. The app also reconciles window positions every two seconds.

The frame clock uses an integer divisor of the primary display’s refresh rate, up to 60 fps on AC power, 30 fps on battery, or 24 fps in lower-frame-rate mode. On a 144 Hz primary display those caps give 48, 28.8, and 24 fps. A busy graphics queue can drop a frame without blocking input. If the device is unavailable or lost, the app uses WPF and caps animation at 30 fps.

Run `tests\Test-Compositor.ps1` in an interactive x64 Windows desktop session to verify GPU composition, transparency, window occlusion, and click-through input. The test briefly opens a blue fixture window. Run `tests\Test-Snow.ps1` for settlement, compaction, melting, and pointer interaction tests. Add `-LiveDesktop` in an interactive x64 session with visible desktop icons and a taskbar to check the Explorer integration.

## Local files

The standalone executable can run from any folder. A per-user installation can place it under `%LOCALAPPDATA%\Programs\Window Weather`. Preferences, saved scenes, and the bird count live in `%LOCALAPPDATA%\Window Weather\settings.json`. The bundled renderer is cached by content hash in the same settings folder under `Renderer`. The app reads window positions and stacking order. Manual scenes work offline.

To remove the installed copy, choose **Quit**, then delete the **Window Weather** desktop shortcut and the **Window Weather** folder under `%LOCALAPPDATA%\Programs`. Removing the settings folder also clears saved scenes and the bird count.
