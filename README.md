# WipShare Client — Phase 1

A Windows tray app that records a 15-second clip of the currently focused
window when you press a global hotkey, then saves it as an H.264 MP4 to
disk. Built for 3D artists who want a one-key way to share work-in-progress
clips with their team.

This is Phase 1 — local recording only. Cloud upload, settings UI, and
account features are explicitly out of scope.

## Hotkey

**`Ctrl + Shift + R`** — globally registered. Press it while focused on any
window (Notepad, Chrome, your 3D viewport) and WipShare records that window
for exactly 15 seconds. You'll see two tray balloons:

1. "Recording 15s…" the moment you press the hotkey.
2. "Saved to wipshare_yyyyMMdd_HHmmss.mp4" once the encoder finalizes.

After step 2 the app opens File Explorer with the new MP4 selected
(`explorer.exe /select,"…"`).

Hotkey presses while a recording is already in flight are ignored and
logged.

## Output

```
%USERPROFILE%\Videos\WipShare\wipshare_yyyyMMdd_HHmmss.mp4
```

The folder is created on first save. Files are H.264 Main profile, ~8 Mbps
target, 30 fps, no audio, MP4 container. They play in Windows Media Player,
the new Media Player app, VLC, mpv, and ffplay.

## Encoder

WipShare picks the best H.264 encoder Media Foundation exposes, in this
order:

1. **NVIDIA NVENC** (`NVIDIA H.264 Encoder MFT`)
2. **AMD AMF**
3. **Intel Quick Sync Video**
4. **Microsoft software H.264 encoder** (fallback)

The choice is logged to `log.txt` at startup:

```
[INF] HardwareEncoderProbe: Hardware H.264 encoder candidate: 'NVIDIA H.264 Encoder MFT' CLSID=… → Nvidia
[INF] App: Selected H.264 encoder: Nvidia 'NVIDIA H.264 Encoder MFT' (CLSID=…, Hardware=True)
```

## Log

```
%LOCALAPPDATA%\WipShare\log.txt
```

Append-only, plain text, one line per event, `Info`-level by default.
Every exception path is logged with its full stack trace.

## Tray menu

Right-click the WipShare tray icon for:

- **Settings** — stub (a hardcoded-defaults notice in Phase 1)
- **About** — version
- **Quit** — exits the app

There is no main window.

## Build

Requirements:

- **.NET 8 SDK** (or newer SDK that can target `net8.0`)
- **Windows 10 SDK 10.0.19041 *or* Windows 11 SDK 10.0.22621** with the
  Platforms/UAP component (the standalone SDK installer or the Visual
  Studio Installer's "Individual Components" tab — _not_ just the
  redistributable runtime, which omits `Platforms\UAP\…\Platform.xml`
  that CsWinRT needs at build time).
- **Windows 10 build 19041 or later** at runtime (`SupportedOSPlatformVersion`).

```powershell
cd WipShare.Client
dotnet restore
dotnet build -c Debug          # smoke build
dotnet publish -c Release -r win-x64 --self-contained
```

The published exe lands at:

```
bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\WipShare.exe
```

(`net8.0-windows10.0.22621.0` is the TFM in `WipShare.Client.csproj`. The
runtime requirement stays at Windows 10 19041 via
`SupportedOSPlatformVersion`; the higher TFM only exists because the
22H2 SDK projection is the one that exposes `IsBorderRequired`, which we
need to hide the yellow capture border on Windows 11.)

A `Properties\PublishProfiles\win-x64.pubxml` is also provided, which adds
`PublishSingleFile=true` + `PublishReadyToRun=true`. Invoke it with:

```powershell
dotnet publish -p:PublishProfile=win-x64
```

## Architecture in one paragraph

`App.xaml.cs` owns the logger, the `MfRuntime` (MFStartup/Shutdown), the
encoder probe, the tray icon, and the global hotkey. A hotkey press
resolves the foreground HWND, rejects WipShare/Progman/WorkerW windows,
and builds a `CaptureSession`. `CaptureSession` owns a `WindowCapture`
(D3D11 device + `GraphicsCaptureItem` via `IGraphicsCaptureItemInterop`
COM + `Direct3D11CaptureFramePool.CreateFreeThreaded`) plus a
`VideoEncoder` (Media Foundation `IMFSinkWriter`, H.264 Main, BGRA input
with the sink writer doing the BGRA→NV12 conversion). A `PeriodicTimer`
inside `CaptureSession` pulls the latest captured surface at 30 Hz and
re-emits the previous frame if the window didn't redraw — so an idle
window still produces a steady-rate MP4.

## Limitations (Phase 1)

By design — these are explicitly _not_ in scope and will not be added
without it being asked for:

- **Window-only.** No region capture, no monitor capture, no fullscreen
  capture.
- **No audio.** Silent video only.
- **No cloud upload.** Files stay on disk.
- **Hardcoded settings.** Duration (15 s), fps (30), bitrate (8 Mbps),
  max resolution (1920 × 1080), hotkey, and output path are all
  compile-time constants in `Settings\AppSettings.cs`. The Settings menu
  is a stub.
- **No accounts, no telemetry, no auto-update, no installer.**

Known practical caveats:

- The captured window's native resolution is capped at 1920 × 1080 with
  aspect preserved. Larger windows are scaled down before encoding.
- Recording is BGRA → SinkWriter → NV12 → H.264, with the pixel readback
  going through `SoftwareBitmap.CreateCopyFromSurfaceAsync`. Fast enough
  for 30 fps at 1080p on modern hardware but it's a CPU/GPU round-trip
  per frame; a future revision should keep frames on the GPU via
  `MFCreateDXGISurfaceBuffer` + an `IMFDXGIDeviceManager` on the sink
  writer.
- Capture fires only when the compositor flips the captured window. A
  totally idle window would produce no frames — `CaptureSession`'s
  timer-driven puller works around this by re-emitting the last frame
  on ticks where no new one arrived.
- Closing the captured window mid-recording isn't gracefully handled
  yet; the session will fail to encode the remaining frames and finalize
  whatever was written so far.
- First-run permission prompts for graphics capture do not appear for
  desktop apps on consumer Windows; if an enterprise policy blocks
  graphics capture, `GraphicsCaptureItem` creation will fail and the
  error surfaces as a tray balloon.
