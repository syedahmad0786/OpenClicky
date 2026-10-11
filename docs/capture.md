# Screen capture

OpenClicky prefers **Windows.Graphics.Capture** with a **Direct3D11** frame pool:

1. Create a D3D11 device (hardware, WARP fallback).
2. Bridge to WinRT `IDirect3DDevice` via `CreateDirect3D11DeviceFromDXGIDevice`.
3. Build a `GraphicsCaptureItem` per monitor (`IGraphicsCaptureItemInterop.CreateForMonitor`).
4. `Direct3D11CaptureFramePool.CreateFreeThreaded` → one frame → staging texture → BGRA → JPEG in memory.

If WGC/D3D init or a monitor capture fails, the service falls back to **GDI BitBlt** for that request and logs `capture_backend=gdi`.

Screenshots are never written to disk. Long edge is downscaled to ~1280px before vision/agent upload.
