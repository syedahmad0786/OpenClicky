# Phase A spike — runbook

## Exit criteria checklist

- [ ] `dotnet build OpenClicky.sln -c Debug` on Windows 11 x64  
- [ ] Tray icon visible  
- [ ] Hold default `Win+Alt+Space` → overlay; release → fade + `capture_bytes` log with JPEG `FFD8`  
- [ ] Remap hotkey in Settings  
- [ ] `dotnet test tests/OpenClicky.Core.Tests`  
- [ ] `npm start` in `services/local-proxy` + `GET /health` → 200  

## Capture note

Phase A uses a **GDI multi-monitor capture** behind `WgcScreenCaptureService` so the silent demo is reliable. A full Windows.Graphics.Capture frame-pool path is the Phase B hardening item (same `IScreenCaptureService` surface).

## App host note

Phase A ships a **WinForms tray host** (`OpenClicky.App`) so the message pump + LL keyboard hook + layered overlay share one STA thread. WinUI 3 settings shell is planned for Phase B once the spike exits.
