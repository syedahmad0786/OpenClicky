import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { ShellState } from "../contract";
import { IntegrationsPage } from "./IntegrationsPage";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const PRIVACY = "When an agent uses the browser, page text, screenshots and its requests go to your model provider. Agents never type passwords and never read cookies or saved passwords. Blocked sites are a convenience, not a guarantee.";

type Integrations = ShellState["settings"]["integrations"];
const make = (patch: (i: Integrations) => void): ShellState => {
  const s = structuredClone(fixture);
  patch(s.settings.integrations);
  return s;
};
const withEdge = (i: Integrations) => {
  i.browser.browsers.push({ id: "edge", name: "Edge", profiles: [{ dir: "Default", name: "Personal" }, { dir: "Profile 2", name: "Work" }] });
};

const postMessage = vi.fn();
function host() {
  postMessage.mockClear();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return () => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === "settings.action");
}
afterEach(() => { cleanup(); delete window.chrome; });

const card = () => screen.getByRole("heading", { name: "Browser" }).closest("section")!;
const click = (name: string) => fireEvent.click(within(card()).getByRole("button", { name }));

it("noBrowser shows download buttons", () => {
  const posts = host();
  render(<IntegrationsPage state={make((i) => { i.chrome = "noBrowser"; i.browser.browsers = []; })} />);
  expect(screen.getByText("No supported browser found")).toBeInTheDocument();
  expect(screen.getByText("OpenClicky works with Chrome, Edge, Brave, Vivaldi and Chromium. Install one, then come back here.")).toBeInTheDocument();
  expect(within(card()).queryByRole("button", { name: "Connect" })).toBeNull();
  click("Get Chrome"); click("Get Edge"); click("Get Brave");
  expect(posts()).toEqual([
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"get","browser":"chrome"}' },
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"get","browser":"edge"}' },
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"get","browser":"brave"}' },
  ]);
});

it("connect posts browser and profile", () => {
  const posts = host();
  render(<IntegrationsPage state={make((i) => { i.chrome = "notInstalled"; withEdge(i); })} />);
  const browser = screen.getByRole("combobox", { name: "Browser" }) as HTMLSelectElement;
  const profile = screen.getByRole("combobox", { name: "Profile" }) as HTMLSelectElement;
  expect(browser.value).toBe("chrome");
  expect([...profile.options].map((o) => o.text)).toEqual(["Ahmad", "Work"]);
  expect(screen.getByText("Agents use the sites you're signed in to in this profile, in their own tab group.")).toBeInTheDocument();

  click("Connect"); // defaults: first browser, its first profile
  fireEvent.change(browser, { target: { value: "edge" } });
  expect([...profile.options].map((o) => o.text)).toEqual(["Personal", "Work"]); // the profile list follows the browser
  expect(profile.value).toBe("Default");
  fireEvent.change(profile, { target: { value: "Profile 2" } });
  click("Connect");
  expect(posts()).toEqual([
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"connect","browser":"chrome","profile":"Default"}' },
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"connect","browser":"edge","profile":"Profile 2"}' },
  ]);
});

it("connect starts from the stored selection", () => {
  const posts = host();
  render(<IntegrationsPage state={make((i) => { i.chrome = "notInstalled"; withEdge(i); i.browser.browserId = "edge"; i.browser.profileDir = "Profile 2"; })} />);
  expect((screen.getByRole("combobox", { name: "Browser" }) as HTMLSelectElement).value).toBe("edge");
  expect((screen.getByRole("combobox", { name: "Profile" }) as HTMLSelectElement).value).toBe("Profile 2");
  click("Connect");
  expect(posts()[0].arg).toBe('{"op":"connect","browser":"edge","profile":"Profile 2"}');
});

it("waiting shows steps and folder", () => {
  const posts = host();
  render(<IntegrationsPage state={make((i) => { i.chrome = "waiting"; })} />);
  expect(screen.getByText("Waiting for the extension…")).toBeInTheDocument();
  const steps = within(card()).getAllByRole("listitem").map((li) => li.textContent);
  expect(steps).toEqual([
    "In the extensions page that opened, turn on Developer mode (top right).",
    "Click Load unpacked and choose this folder:" + fixture.settings.integrations.browser.extensionFolder + "Copy pathShow folder",
    "That's it. This card turns green when the extension says hello.",
  ]);
  expect(within(card()).getByText(fixture.settings.integrations.browser.extensionFolder).tagName).toBe("CODE");
  click("Copy path"); click("Show folder"); click("Open extensions page"); click("Cancel");
  expect(posts()).toEqual([
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"copyPath"}' },
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"showFolder"}' },
    { kind: "settings.action", action: "chrome.setup", arg: '{"op":"openExtensions"}' },
    { kind: "settings.action", action: "chrome.setup", arg: "disconnect" },
  ]);
});

it("connected and working show the badge, the browser and profile, and Disconnect", () => {
  const posts = host();
  const known = (c: Integrations["chrome"]) => make((i) => { i.chrome = c; i.browser.browserId = "chrome"; i.browser.profileDir = "Profile 2"; });
  const { rerender } = render(<IntegrationsPage state={known("connected")} />);
  expect(within(card()).getByText("Connected", { selector: ".oc-badge" })).toBeInTheDocument();
  expect(within(card()).getByText("Chrome · Work")).toBeInTheDocument();
  expect(within(card()).queryByText("In use")).toBeNull(); // no instance rows for a single browser profile
  rerender(<IntegrationsPage state={known("working")} />);
  expect(within(card()).getByText("Working", { selector: ".oc-badge" })).toBeInTheDocument();
  click("Disconnect");
  expect(posts()).toEqual([{ kind: "settings.action", action: "chrome.setup", arg: "disconnect" }]);
});

it("the browser line is left out when the names are unknown", () => {
  host();
  const unknown = (browserId: string | null, profileDir: string | null) =>
    make((i) => { i.chrome = "connected"; i.browser.browserId = browserId; i.browser.profileDir = profileDir; });
  const { rerender } = render(<IntegrationsPage state={unknown("chrome", "Profile 9")} />);
  expect(within(card()).queryByText(/·/)).toBeNull();
  rerender(<IntegrationsPage state={unknown("vivaldi", "Default")} />);
  expect(within(card()).queryByText(/·/)).toBeNull();
  rerender(<IntegrationsPage state={unknown(null, null)} />);
  expect(within(card()).queryByText(/·/)).toBeNull();
});

it("several instances can be switched", () => {
  const posts = host();
  render(<IntegrationsPage state={make((i) => {
    i.chrome = "connected";
    i.browser.instances = [{ id: "p1", label: "Chrome - Ahmad", selected: true }, { id: "p2", label: "Chrome - Work", selected: false }];
  })} />);
  expect(within(card()).getByText("Chrome - Ahmad")).toBeInTheDocument();
  expect(within(card()).getAllByText("In use")).toHaveLength(1);
  expect(within(card()).getAllByRole("button", { name: "Use this one" })).toHaveLength(1);
  click("Use this one");
  expect(posts()).toEqual([{ kind: "settings.action", action: "chrome.setup", arg: '{"op":"select","id":"p2"}' }]);
});

it("privacy line always shown", () => {
  host();
  const states: Integrations["chrome"][] = ["notInstalled", "waiting", "connected", "working", "noBrowser"];
  for (const chrome of states) {
    const { unmount } = render(<IntegrationsPage state={make((i) => { i.chrome = chrome; })} />);
    const line = within(card()).getByText(PRIVACY);
    expect(line.tagName).toBe("P");
    expect(line).toHaveClass("settings-line");
    unmount();
  }
});
