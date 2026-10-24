import { postPage } from "../bridge";
import type { ShellState } from "../contract";
import { Badge, Group, PillButton, RadioList, Row, Segmented } from "../ui";
import { act, Page, patch, useDraft } from "./common";

const PROVIDERS = [{ value: "chatgpt", label: "ChatGPT plan" }, { value: "openrouter", label: "OpenRouter" }] as const;

function ModelPicker({ name, label, current, models, onPick }: { name: string; label: string; current: string; models: string[]; onPick: (m: string) => void }) {
  const ids = [...new Set([current, ...models].filter(Boolean))];
  return ids.length ? (
    <RadioList name={name} label={label} value={current} options={ids.map((m) => ({ value: m, label: m }))} onChange={onPick} />
  ) : <Row title="No models loaded yet" description="Use Refresh models to fetch the list." />;
}

export function AccountPage({ state }: { state: ShellState }) {
  const a = state.settings.account;
  const [orModel, setOrModel] = useDraft(a.openRouterModel);
  return (
    <Page title="Account" subtitle="Who OpenClicky works as, and which model it uses.">
      <Group title="Model provider">
        <Row title="Provider" description="Where OpenClicky's answers come from.">
          <Segmented label="Model provider" options={PROVIDERS} value={a.provider} onChange={(v) => patch("account.provider", v)} />
        </Row>
      </Group>
      {a.provider === "chatgpt" ? (
        <>
          <Group title="ChatGPT">
            {a.signedInAs ? (
              <Row title={`Signed in as ${a.signedInAs}`}><PillButton onClick={() => act("signOut")}>Sign out</PillButton></Row>
            ) : (
              <Row title="Not signed in" description="Sign in to use your ChatGPT plan."><PillButton variant="primary" onClick={() => act("signIn")}>Continue with ChatGPT</PillButton></Row>
            )}
          </Group>
          <Group title="Model">
            <ModelPicker name="account-model" label="Model" current={a.model} models={a.models} onPick={(m) => patch("account.model", m)} />
          </Group>
          <Group title="Quick-reply model">
            <ModelPicker name="account-quick-model" label="Quick-reply model" current={a.quickModel} models={a.models} onPick={(m) => patch("account.quickModel", m)} />
          </Group>
          <Group title="Usage">
            <Row title="Refresh models" description="Fetch the models your plan can use."><PillButton onClick={() => act("refreshModels")}>Refresh models</PillButton></Row>
            <Row title="Manage usage" description="See your plan's limits in the browser."><PillButton onClick={() => act("manageUsage")}>Manage usage</PillButton></Row>
          </Group>
        </>
      ) : (
        <Group title="OpenRouter">
          <Row title="API key" description="Stored in Windows Credential Manager, never shown here.">
            <Badge tone={a.openRouterKeySet ? "ok" : "muted"}>{a.openRouterKeySet ? "Saved" : "Not set"}</Badge>
            <PillButton onClick={() => act("secret.edit", "openrouter")}>Change</PillButton>
          </Row>
          <Row title="Model" description="An OpenRouter model id, for example vendor/model-name.">
            <input className="settings-input" aria-label="OpenRouter model" maxLength={120} value={orModel} spellCheck={false}
              onChange={(e) => setOrModel(e.target.value)}
              onBlur={() => { if (orModel !== a.openRouterModel) patch("account.openRouterModel", orModel); }} />
          </Row>
        </Group>
      )}
      <Group title="App">
        <Row title="Quit OpenClicky" description="Closes OpenClicky and stops anything it is running." tone="danger">
          <PillButton variant="danger" aria-label="Quit OpenClicky" onClick={() => postPage({ kind: "app.quit" })}>Quit</PillButton>
        </Row>
      </Group>
    </Page>
  );
}
