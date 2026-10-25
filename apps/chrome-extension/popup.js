const LINES = {
  checking: "Checking the connection…",
  setup: "Finish setup in OpenClicky: Settings → Integrations → Browser.",
  offline: "OpenClicky isn't running.",
  profile: "Another browser profile is connected to OpenClicky.",
  connected: "Connected to OpenClicky.",
  working: "Working.",
};

const el = (tag, props = {}, text) => {
  const node = Object.assign(document.createElement(tag), props);
  if (text !== undefined) node.textContent = text; // textContent only: titles and say lines are never HTML
  return node;
};

async function send(message) {
  render(await chrome.runtime.sendMessage(message));
}

function button(label, message, primary) {
  return el("button", { className: primary ? "primary" : "", onclick: () => send(message) }, label);
}

function render(status) {
  if (!status) return;
  document.getElementById("state").textContent = LINES[status.state] ?? LINES.checking;
  const actions = document.getElementById("actions");
  actions.replaceChildren();
  if (status.state === "offline") actions.append(button("Open OpenClicky", { type: "openApp" }, true));
  if (status.state === "profile") actions.append(button("Use this profile", { type: "select" }, true));
  if (status.state === "working") {
    actions.append(button("Stop", { type: "stop" }, true), el("p", { className: "note" }, "Stop hands the tabs to you and closes nothing."));
  }
  const jobs = document.getElementById("jobs");
  jobs.replaceChildren(...(status.jobs ?? []).slice(0, 3).map((job) => {
    const box = el("div", { className: "job" });
    box.append(el("div", { className: "title" }, job.title || "Job"));
    if (job.say) box.append(el("div", { className: "say" }, job.say));
    for (const tab of (job.tabs ?? []).slice(0, 4)) box.append(button(tab.title, { type: "focusTab", tabId: tab.id }));
    return box;
  }));
}

send({ type: "status" });
setInterval(() => send({ type: "status" }), 1500);
