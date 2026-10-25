import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { ShellState } from "../contract";
import { MAX_CONNECTOR_ADD_ARG, type Connector } from "../connectorsContract";
import { SettingsPanel } from "./SettingsPanel";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;

/** The fixture plus Notion and Composio in the catalog (a connected multi-account app, and the "url" setup). */
const make = (patch: (s: ShellState) => void = () => {}): ShellState => {
  const s = structuredClone(fixture);
  s.notch = { ...s.notch, tab: "settings", open: true, section: "integrations" };
  s.settings.integrations.catalog.unshift(
    { id: "notion", name: "Notion", description: "Pages and databases.", setup: "signIn", multiAccount: true },
    { id: "composio", name: "Composio", description: "Your own Composio MCP address and key.", setup: "url", multiAccount: false },
  );
  patch(s);
  return s;
};
const conn = (c: Partial<Connector> & Pick<Connector, "key" | "name">): Connector => ({
  host: "mcp.example.com", status: "connected", toolCount: null, app: null, label: null, kind: "remote", auth: "none", since: null, ...c,
});
const notion = (n: number) => (s: ShellState) => {
  const labels = ["Work", "Personal", "Home", "Side", "Club"];
  s.settings.integrations.connectors = s.settings.integrations.connectors.filter((c) => c.app !== "notion");
  for (let i = 0; i < n; i++) s.settings.integrations.connectors.push(conn({ key: i === 0 ? "notion" : `notion_${i + 1}`, name: "Notion", app: "notion", label: labels[i], auth: "browser", host: "mcp.notion.com" }));
};

const postMessage = vi.fn();
function host() {
  postMessage.mockClear();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return () => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === "settings.action");
}
const sent = (action: string, arg?: unknown) => ({ kind: "settings.action", action, ...(arg === undefined ? {} : { arg: typeof arg === "string" ? arg : JSON.stringify(arg) }) });
afterEach(() => { cleanup(); delete window.chrome; });

const section = (title: string) => screen.getByRole("heading", { name: title }).closest("section") as HTMLElement;
const openEditor = () => fireEvent.click(screen.getByRole("button", { name: "Add custom connector" }));
const openAdvanced = () => fireEvent.click(within(section("New connector")).getByRole("button", { name: "Advanced" })); // the sidebar has an Advanced page too
const type = (name: string, value: string) => fireEvent.change(screen.getByRole("textbox", { name }), { target: { value } });

it("GroupsConnectedAndAvailable", () => {
  host();
  render(<SettingsPanel state={make()} />);
  const connected = within(section("Connected"));
  expect(connected.getByText("Notion · Work")).toBeInTheDocument();
  expect(connected.getByText("Slack")).toBeInTheDocument();
  const available = within(section("Available"));
  expect(available.getByText("Linear")).toBeInTheDocument();
  expect(available.getByText("Obsidian")).toBeInTheDocument();
  expect(available.getByText("Composio")).toBeInTheDocument();
  expect(available.queryByText("Notion")).toBeNull();
  expect(connected.getByText("12 tools")).toBeInTheDocument();
  // The page shows the local date; 2026-10-20T09:00Z is 19, 20 or 21 Oct depending on the machine's time zone.
  expect(connected.getByText(`Added ${new Date("2026-10-20T09:00:00Z").getDate()} Oct 2026`)).toBeInTheDocument();
});

it("Empty sections say so", () => {
  host();
  render(<SettingsPanel state={make((s) => { s.settings.integrations.connectors = []; s.settings.integrations.catalog = []; })} />);
  expect(screen.getByText("Nothing connected yet.")).toBeInTheDocument();
  expect(screen.getByText("Everything here is connected.")).toBeInTheDocument();
});

it.each([
  ["connected", "Connected", "ok"], ["checking", "Checking…", "muted"], ["configured", "Not checked", "muted"], ["signingIn", "Signing in…", "muted"],
  ["needsSignIn", "Needs sign-in", "warn"], ["reconnect", "Reconnect required", "warn"], ["tokenRejected", "Key rejected", "danger"], ["error", "Error", "danger"],
] as const)("StatusChips: %s", (status, text, tone) => {
  host();
  render(<SettingsPanel state={make((s) => { s.settings.integrations.connectors = [conn({ key: "x", name: "X", status })]; })} />);
  const chip = within(section("Connected")).getByText(text, { selector: ".oc-badge" });
  expect(chip).toHaveClass(`oc-badge--${tone}`);
});

it("SignInButtonByStatus", () => {
  const posts = host();
  const { rerender } = render(<SettingsPanel state={make()} />);
  fireEvent.click(screen.getByRole("button", { name: "Reconnect Slack" }));
  expect(posts()).toEqual([sent("connector.signIn", "slack")]);
  expect(screen.getByRole("button", { name: "Test Slack" })).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Sign in Notion · Work" })).toBeNull(); // connected

  rerender(<SettingsPanel state={make((s) => { s.settings.integrations.connectors[1].status = "needsSignIn"; })} />);
  fireEvent.click(screen.getByRole("button", { name: "Sign in Slack" }));
  expect(posts()).toEqual([sent("connector.signIn", "slack"), sent("connector.signIn", "slack")]);

  rerender(<SettingsPanel state={make((s) => { s.settings.integrations.connectors[1].status = "signingIn"; })} />);
  expect(screen.getByText("Finish connecting in your browser.")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Test Slack" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Sign in Slack" })).toBeNull();

  rerender(<SettingsPanel state={make((s) => { s.settings.integrations.connectors[1].status = "checking"; })} />);
  expect(screen.queryByRole("button", { name: "Test Slack" })).toBeNull();

  // An API-key row never offers browser sign-in.
  rerender(<SettingsPanel state={make((s) => { Object.assign(s.settings.integrations.connectors[1], { status: "needsSignIn", auth: "apiKey" }); })} />);
  expect(screen.queryByRole("button", { name: "Sign in Slack" })).toBeNull();
});

it("AddAnotherAccount", () => {
  const posts = host();
  const { rerender } = render(<SettingsPanel state={make()} />);
  fireEvent.click(screen.getByRole("button", { name: "Add another Notion account" }));
  fireEvent.click(screen.getByRole("radio", { name: "Personal" }));
  fireEvent.click(screen.getByRole("button", { name: "Continue" }));
  expect(posts()).toEqual([sent("connector.add", { app: "notion", label: "Personal" })]);
  expect(screen.queryByRole("button", { name: "Continue" })).toBeNull();

  // Other… asks for a name of up to 30 characters.
  fireEvent.click(screen.getByRole("button", { name: "Add another Notion account" }));
  fireEvent.click(screen.getByRole("radio", { name: "Other…" }));
  const continueButton = screen.getByRole("button", { name: "Continue" });
  expect(continueButton).toBeDisabled();
  type("Label", "x".repeat(31));
  expect(screen.getByRole("alert")).toHaveTextContent("Keep the label to 30 characters.");
  expect(continueButton).toBeDisabled();
  type("Label", " Side project ");
  fireEvent.click(continueButton);
  expect(posts()[1]).toEqual(sent("connector.add", { app: "notion", label: "Side project" }));

  // Cancel posts nothing.
  fireEvent.click(screen.getByRole("button", { name: "Add another Notion account" }));
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(posts()).toHaveLength(2);

  rerender(<SettingsPanel state={make(notion(5))} />);
  expect(screen.getAllByText(/^Notion · /)).toHaveLength(5);
  expect(screen.queryByRole("button", { name: "Add another Notion account" })).toBeNull();
});

it("Account rows of one app sit together", () => {
  host();
  render(<SettingsPanel state={make((s) => { s.settings.integrations.connectors.push(conn({ key: "notion_2", name: "Notion", app: "notion", label: "Personal" })); })} />);
  const titles = [...section("Connected").querySelectorAll(".oc-row__title")].map((e) => e.textContent);
  expect(titles).toEqual(["Notion · Work", "Notion · Personal", "Slack"]);
  const addAnother = screen.getByRole("button", { name: "Add another Notion account" });
  expect(screen.getByText("Notion · Personal").compareDocumentPosition(addAnother) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  expect(addAnother.compareDocumentPosition(screen.getByText("Slack")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
});

it("RenameLabel", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  expect(screen.queryByRole("button", { name: "Rename Slack" })).toBeNull(); // custom connectors have no label
  fireEvent.click(screen.getByRole("button", { name: "Rename Notion · Work" }));
  expect(screen.getByRole("radio", { name: "Work" })).toHaveAttribute("aria-checked", "true");
  fireEvent.click(screen.getByRole("radio", { name: "Personal" }));
  fireEvent.click(screen.getByRole("button", { name: "Save" }));
  expect(posts()).toEqual([sent("connector.label", { key: "notion", label: "Personal" })]);
});

it("ConnectAvailable", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  fireEvent.click(screen.getByRole("button", { name: "Connect Linear" }));
  fireEvent.click(screen.getByRole("button", { name: "Choose vault" }));
  expect(posts()).toEqual([sent("connector.add", { app: "linear" }), sent("connector.add", { app: "obsidian" })]);

  fireEvent.click(screen.getByRole("button", { name: "Set up Composio" }));
  expect(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Composio");
  expect(screen.getByRole("radio", { name: "API key" })).toHaveAttribute("aria-checked", "true");
  expect(screen.getByText("Paste the MCP address from your Composio dashboard.")).toBeInTheDocument();
  type("Server URL", "https://mcp.composio.dev/abc/mcp");
  fireEvent.click(screen.getByRole("button", { name: "Add connector" }));
  expect(posts()[2]).toEqual(sent("connector.add", { app: "composio", name: "Composio", kind: "remote", url: "https://mcp.composio.dev/abc/mcp", auth: "apiKey" }));
});

it("Vault row disconnects with its own confirm", () => {
  const posts = host();
  render(<SettingsPanel state={make((s) => { s.settings.integrations.connectors.push(conn({ key: "obsidian", name: "Obsidian", app: "obsidian", kind: "vault", host: "Notes", toolCount: 4 })); })} />);
  expect(within(section("Available")).queryByText("Obsidian")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Disconnect Obsidian" }));
  expect(screen.getByRole("alert")).toHaveTextContent("Disconnect Obsidian? Agents lose access to your notes from their next task.");
  fireEvent.click(screen.getByRole("button", { name: "Disconnect" }));
  expect(posts()).toEqual([sent("connector.remove", "obsidian")]);
});

it("SearchFiltersBothSections", () => {
  host();
  render(<SettingsPanel state={make()} />);
  const search = screen.getByRole("searchbox", { name: "Search integrations" });
  fireEvent.change(search, { target: { value: "lin" } });
  expect(screen.getByText("Linear")).toBeInTheDocument();
  expect(screen.queryByText("Obsidian")).toBeNull();
  expect(screen.queryByText("Notion · Work")).toBeNull();
  fireEvent.change(search, { target: { value: "slack.com" } });
  expect(screen.getByText("Slack")).toBeInTheDocument();
  expect(screen.queryByText("Linear")).toBeNull();
  fireEvent.change(search, { target: { value: "zzz" } });
  expect(screen.getByText("Nothing matches “zzz”.")).toBeInTheDocument();
  expect(screen.queryByText("Slack")).toBeNull();
});

it("EditorRemoteBrowser", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  expect(screen.getByRole("radio", { name: "Remote URL" })).toHaveAttribute("aria-checked", "true");
  expect(screen.getByRole("radio", { name: "Sign in with browser" })).toHaveAttribute("aria-checked", "true");
  expect(screen.getByText("Opens your browser to sign in. OpenClicky never sees your password.")).toBeInTheDocument();
  type("Name", " Design ");
  type("Server URL", "https://mcp.example.com/mcp");
  openAdvanced();
  expect(screen.getByText(/http:\/\/127\.0\.0\.1:47613\/callback/)).toBeInTheDocument();
  expect(screen.getByText("Saved encrypted on this PC and never shown again. To change them, remove the connector and add it again.")).toBeInTheDocument();
  type("OAuth client ID", " abc123 ");
  type("Extra headers", "X-Team: design\nX-Env: prod");
  fireEvent.click(screen.getByRole("button", { name: "Add connector" }));
  expect(posts()).toEqual([sent("connector.add", { name: "Design", kind: "remote", url: "https://mcp.example.com/mcp", auth: "browser", clientId: "abc123", headers: "X-Team: design\nX-Env: prod" })]);
  expect(screen.getByRole("button", { name: "Adding…" })).toBeDisabled();
});

it("Editor hints follow the authentication; client ID is only for browser sign-in", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  openAdvanced();
  type("OAuth client ID", "abc");
  fireEvent.click(screen.getByRole("radio", { name: "API key" }));
  expect(screen.getByText("OpenClicky asks for the key in a secure prompt after you add the connector.")).toBeInTheDocument();
  expect(screen.queryByRole("textbox", { name: "OAuth client ID" })).toBeNull();
  fireEvent.click(screen.getByRole("radio", { name: "None" }));
  expect(screen.getByText("For servers that need no account.")).toBeInTheDocument();
  type("Name", "Open");
  type("Server URL", "https://open.example.com/mcp");
  fireEvent.click(screen.getByRole("button", { name: "Add connector" }));
  expect(posts()).toEqual([sent("connector.add", { name: "Open", kind: "remote", url: "https://open.example.com/mcp", auth: "none" })]);
});

it("EditorLocalCommand", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  fireEvent.click(screen.getByRole("radio", { name: "Command on this PC" }));
  expect(screen.queryByRole("textbox", { name: "Server URL" })).toBeNull();
  expect(screen.queryByRole("radiogroup", { name: "Authentication" })).toBeNull();
  expect(screen.getByText("OpenClicky starts this program on your PC when an agent needs it. Only add programs you trust.")).toBeInTheDocument();
  expect(screen.getByRole("textbox", { name: "Command" })).toHaveAttribute("placeholder", "npx");
  type("Name", "Files");
  const add = screen.getByRole("button", { name: "Add connector" });
  expect(add).toBeDisabled(); // no command yet
  type("Command", "npx");
  type("Arguments", "-y\n@modelcontextprotocol/server-filesystem\n");
  openAdvanced();
  expect(screen.queryByRole("textbox", { name: "Extra headers" })).toBeNull();
  expect(screen.queryByRole("textbox", { name: "OAuth client ID" })).toBeNull();
  type("Environment variables", "API_TOKEN=s3cret");
  fireEvent.click(add);
  expect(posts()).toEqual([sent("connector.add", { name: "Files", kind: "local", command: "npx", args: "-y\n@modelcontextprotocol/server-filesystem\n", env: "API_TOKEN=s3cret" })]);
});

it("EditorValidation", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  type("Name", "Bad");
  type("Server URL", "https://mcp.example.com/mcp");
  openAdvanced();
  const add = screen.getByRole("button", { name: "Add connector" });
  expect(add).toBeEnabled();

  for (const [headers, error] of [
    ["no colon here", "Write each header as Name: value on its own line."],
    ["Bad Name: x", "Write each header as Name: value on its own line."],
    ["X-Empty:", "Write each header as Name: value on its own line."],
    ["Host: evil", "Header Host can't be set here."],
    ["Authorization: Bearer x", "Header Authorization can't be set here."],
    ["X-A: 1\nx-a: 2", "Header x-a is listed twice."],
    [Array.from({ length: 17 }, (_, i) => `X-${i}: v`).join("\n"), "Up to 16 headers."],
  ]) {
    type("Extra headers", headers);
    expect(screen.getByRole("alert")).toHaveTextContent(error);
    expect(add).toBeDisabled();
  }

  // Authorization is the user's own business when the connector has no auth.
  fireEvent.click(screen.getByRole("radio", { name: "None" }));
  type("Extra headers", "Authorization: Bearer x");
  expect(add).toBeEnabled();

  type("Extra headers", `X-Big: ${"v".repeat(3_500)}\nX-Big2: ${"v".repeat(3_500)}`);
  expect(add).toBeEnabled();
  type("Extra headers", Array.from({ length: 3 }, (_, i) => `X-${i}: ${"v".repeat(3_000)}`).join("\n"));
  expect(screen.getByRole("alert")).toHaveTextContent("That's too much to send at once. Shorten the headers or variables.");
  expect(add).toBeDisabled();
  fireEvent.click(add);
  expect(posts()).toEqual([]);
  expect(JSON.stringify({ headers: "x".repeat(MAX_CONNECTOR_ADD_ARG) }).length).toBeGreaterThan(MAX_CONNECTOR_ADD_ARG);
});

it("A header or variable error stays visible with Advanced collapsed", () => {
  host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  type("Name", "Mine");
  type("Server URL", "https://mcp.example.com/mcp");
  fireEvent.click(screen.getByRole("radio", { name: "None" }));
  openAdvanced();
  type("Extra headers", "Authorization: Bearer x");
  openAdvanced(); // collapse
  const add = screen.getByRole("button", { name: "Add connector" });
  expect(add).toBeEnabled();
  expect(screen.queryByRole("alert")).toBeNull();
  fireEvent.click(screen.getByRole("radio", { name: "API key" }));
  expect(screen.getByRole("alert")).toHaveTextContent("Header Authorization can't be set here.");
  expect(add).toBeDisabled();

  fireEvent.click(screen.getByRole("radio", { name: "Command on this PC" }));
  type("Command", "npx");
  openAdvanced();
  type("Environment variables", "NO_EQUALS");
  openAdvanced(); // collapse
  expect(screen.getByRole("alert")).toHaveTextContent("Write each variable as NAME=value on its own line.");
  expect(add).toBeDisabled();
});

it("Editor local validation mirrors the host", () => {
  host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  fireEvent.click(screen.getByRole("radio", { name: "Command on this PC" }));
  type("Name", "Local");
  type("Command", "npx");
  openAdvanced();
  const add = screen.getByRole("button", { name: "Add connector" });
  for (const [env, error] of [
    ["NO_EQUALS", "Write each variable as NAME=value on its own line."],
    ["1BAD=x", "Write each variable as NAME=value on its own line."],
    ["A=1\na=2", "Variable a is listed twice."],
    [Array.from({ length: 33 }, (_, i) => `V${i}=x`).join("\n"), "Up to 32 variables."],
  ]) {
    type("Environment variables", env);
    expect(screen.getByRole("alert")).toHaveTextContent(error);
    expect(add).toBeDisabled();
  }
  type("Environment variables", "EMPTY=");
  expect(add).toBeEnabled();
  type("Arguments", Array.from({ length: 33 }, (_, i) => `a${i}`).join("\n"));
  expect(screen.getByRole("alert")).toHaveTextContent("Up to 32 arguments.");
  type("Arguments", "x".repeat(1_001));
  expect(screen.getByRole("alert")).toHaveTextContent("Each argument must be up to 1,000 characters, without control characters.");
  type("Arguments", "ok");
  type("Name", "x".repeat(61));
  expect(screen.getByRole("alert")).toHaveTextContent("Give the connector a name of up to 60 characters.");
  expect(add).toBeDisabled();
});

it("Typed secrets are dropped when the editor closes", () => {
  host();
  render(<SettingsPanel state={make()} />);
  openEditor();
  openAdvanced();
  type("Extra headers", "X-Key: secret");
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  openEditor();
  openAdvanced();
  expect(screen.getByRole("textbox", { name: "Extra headers" })).toHaveValue("");
});

it("ShowsWhatLeavesThisPc", () => {
  host();
  render(<SettingsPanel state={make()} />);
  expect(screen.getByText("When an agent uses an app, its request goes to that app, and what comes back goes to your model provider with the task. Keys and sign-ins stay encrypted on this PC.")).toBeInTheDocument();
  expect(screen.getByText("Apps and services your agents can use.")).toBeInTheDocument();
});
