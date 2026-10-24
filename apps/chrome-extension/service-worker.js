const HOST = "com.openclicky.chrome_bridge";
let nativePort;

function connect() {
  if (nativePort) return nativePort;
  nativePort = chrome.runtime.connectNative(HOST);
  nativePort.onDisconnect.addListener(() => { nativePort = undefined; setTimeout(connect, 1000); });
  nativePort.onMessage.addListener(async message => {
    if (!message?.requestId) return;
    try {
      if (message.command?.name === "contextActive") {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        if (!Number.isInteger(tab?.id)) throw new Error("No permitted active Chrome tab is available.");
        const response = await chrome.tabs.sendMessage(tab.id, { name: "context" }, { frameId: 0 });
        nativePort?.postMessage({ requestId: message.requestId, tabId: tab.id, frameId: 0, response });
        return;
      }
      if (message.command?.name === "observeActive") {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        if (!Number.isInteger(tab?.id)) throw new Error("No active Chrome tab is available.");
        const response = await chrome.tabs.sendMessage(tab.id, { name: "observe" }, { frameId: 0 });
        nativePort?.postMessage({ requestId: message.requestId, tabId: tab.id, frameId: 0, response });
        return;
      }
      if (!Number.isInteger(message.tabId) || !Number.isInteger(message.frameId)) throw new Error("An exact observed tab and frame are required.");
      const response = await chrome.tabs.sendMessage(message.tabId, message.command, { frameId: message.frameId });
      nativePort?.postMessage({ requestId: message.requestId, tabId: message.tabId, frameId: message.frameId, response });
    } catch (error) {
      nativePort?.postMessage({ requestId: message.requestId, error: String(error?.message || error) });
    }
  });
  return nativePort;
}

chrome.runtime.onInstalled.addListener(connect);
chrome.runtime.onStartup.addListener(connect);
