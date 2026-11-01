# OpenClicky Browser Companion

An unpacked Chrome/Edge extension that lets OpenClicky agents work in your signed-in browser. Each job gets its own tab group; an agent reads pages as accessibility snapshots, clicks, fills and types through Chrome's debugging protocol (`chrome.debugger`), and reports what changed after every action. The popup shows the connection, the jobs at work and their tabs, and a **Stop** button that hands every tab back to you and closes nothing.

## Permissions and why

| Permission | Why |
| --- | --- |
| `debugger` | Read pages and act in them (snapshots, clicks, typing, screenshots). Chrome shows its "started debugging this browser" bar while it is attached; cancelling that bar stops the job. |
| `tabs`, `tabGroups` | List tabs, open new ones and keep each job's tabs in its own group. |
| `nativeMessaging` | Talk to OpenClicky on this PC through the `com.openclicky.chrome_bridge` native host. |
| `storage` | A random profile id (so OpenClicky can tell browser profiles apart) and which tabs belong to which job. |
| `alarms` | Reconnect to OpenClicky every 30 seconds when the connection drops. |
| `downloads` | Save files an agent downloads under `Downloads/OpenClicky/<job>/`. |

There are no host permissions and no content scripts. Incognito windows are off limits.

## What leaves the machine

When an agent uses the browser, the page text, snapshots, screenshots, tab titles and addresses, and the agent's requests go to your model provider, like any other agent turn. Uploads send the files you approved to the website. Nothing else: native messaging and the pipe to OpenClicky stay on this PC.

## What it never does

- It never types into a password field: `fill`, `type`, batch steps and scripts are refused there. Ask the user to type passwords, codes and card numbers themselves.
- It never reads cookies or saved passwords. Agent scripts run in an isolated world where cookies and site storage are unavailable and password values read as empty.
- Sites on the blocked list (OpenClicky's defaults plus your own fragments) are refused before anything is read or changed. The list is a convenience, not a guarantee.

## Load it unpacked

1. In OpenClicky, open **Settings → Integrations → Browser** and follow the steps; it registers the native host for your user and shows this folder.
2. Open `chrome://extensions` (or `edge://extensions`), turn on **Developer mode** and choose **Load unpacked**.
3. Select this folder. The card in OpenClicky turns green when the extension says hello.

The `key` in `manifest.json` gives the extension the stable id `demdidhcdmmfbpidanflhknhplklkcja`, which the native host allows. It is an identity, not a secret.

## Files

`service-worker.js` connects to the native host and the popup; `core.mjs` validates and runs each command; `page.mjs`, `inpage.mjs` and `snapshot.mjs` work inside one tab; `tabs.mjs` owns the tab groups. Tests live in `apps/companion-workspace/src/chrome-extension/`.
