# Optional Chrome bridge

This unpacked MV3 extension lets one local OpenClicky companion inspect and act in existing signed-in Chrome tabs. Page titles, visible control text, typed text, and requested actions leave Chrome through Chrome native messaging and are available to the local OpenClicky process. The extension contains no provider credential or pairing secret.

1. Build/publish OpenClicky with native-host mode wired to `ChromeBridgeNativeProtocol`.
2. Run `setup-local.ps1 -NativeHostPath <absolute OpenClicky host exe> -AllowedOrigins https://example.com/`.
3. Open `chrome://extensions`, enable Developer mode, choose **Load unpacked**, and select `%LOCALAPPDATA%\OpenClicky\ChromeBridge`.

The checked-in public extension key gives local builds the stable ID `demdidhcdmmfbpidanflhknhplklkcja`; it is identity, not a secret. Setup writes a per-user Chrome native-host registration and generates exact site matches plus that exact `chrome-extension://…/` native-host allowlist. Add each required origin explicitly. It does not install, enable, or authorize the extension for you.

Setup also writes non-secret `%LOCALAPPDATA%\OpenClicky\ChromeBridge\bridge-config.json`. The application loads it with `ChromeBridgeConfiguration.Load()` so its runtime origin policy exactly matches the generated extension permissions.

The application must reject excluded origins/titles before requesting an observation and immediately before every action. Actions use only element/link IDs from the current tab, frame, and navigation revision; there is no raw URL, selector, file, or shell command surface.

Native request contract: send `{ requestId, command: { name: "observeActive" } }`; the extension returns the exact active `tabId`, top `frameId`, and synthetic page observation. Follow-up commands must repeat those IDs and the returned revision. The application owns monotonic navigation revision tracking and must discard a snapshot after every attempted mutation.
