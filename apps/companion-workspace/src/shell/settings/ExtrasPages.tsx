import { useEffect, useRef, useState, type ReactNode } from "react";
import type { ShellState } from "../contract";
import { Badge, ChevronIcon, Group, PillButton, Row, SearchField, Toggle } from "../ui";
import { act, ConfirmRow, Page, useDraft } from "./common";

type Settings = ShellState["settings"];
type Note = Settings["notes"]["items"][number];
type Clip = Settings["clipboard"]["items"][number];
type Run = Settings["tasks"]["runs"][number];
type Fact = Settings["memory"]["facts"][number];

/**
 * A Save that waits for the host: busy until `watch` (a key of the data it changes) moves, then `done` runs
 * (clear the field). If nothing moves within 4 s the host refused and said why in a toast: the draft stays for a retry.
 */
function useSave(watch: string) {
  const [from, setFrom] = useState<string | null>(null);
  const done = useRef<() => void>(() => {});
  useEffect(() => {
    if (from === null) return;
    if (watch !== from) { setFrom(null); done.current(); return; }
    const timer = setTimeout(() => setFrom(null), 4_000);
    return () => clearTimeout(timer);
  }, [watch, from]);
  const save = (send: () => void, then: () => void = () => {}) => { done.current = then; setFrom(watch); send(); };
  return [from !== null, save] as const;
}

const matches = (query: string, ...fields: (string | null)[]) => {
  const q = query.trim().toLocaleLowerCase();
  return !q || fields.some((f) => f?.toLocaleLowerCase().includes(q));
};

const shortDate = (iso: string) => {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleString(undefined, { day: "numeric", month: "short", hour: "numeric", minute: "2-digit" });
};

const oneLine = (text: string) => text.replace(/\s+/g, " ").trim();

/** The page's data could not be read: say so once, above everything, and leave the controls disabled. */
const Locked = ({ error }: { error: string }) => (
  <Group><Row title="Editing is locked" description={error} tone="danger" /></Group>
);

function Field({ id, label, hint, children }: { id: string; label: string; hint?: string; children: ReactNode }) {
  return (
    <div className="settings-field">
      <label className="settings-field__label" htmlFor={id}>{label}</label>
      {hint ? <span className="settings-field__hint">{hint}</span> : null}
      {children}
    </div>
  );
}

/** One list entry: its row, its buttons, and a Delete that asks first when `confirm` is set (focus goes back to Delete on Cancel). */
function Item({ name, row, actions, confirm, onDelete, disabled }: {
  name: string; row: ReactNode; actions?: ReactNode; confirm?: string; onDelete: () => void; disabled?: boolean;
}) {
  const [confirming, setConfirming] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  return (
    <div className="settings-item">
      {row}
      <div className="settings-actions">
        {actions}
        <PillButton size="sm" variant="danger" ref={trigger} aria-label={`Delete ${name}`} disabled={disabled}
          onClick={() => (confirm ? setConfirming(true) : onDelete())}>Delete</PillButton>
      </div>
      {confirming && confirm ? (
        <ConfirmRow title={confirm} keepLabel="Cancel" confirmLabel="Delete"
          onKeep={() => { setConfirming(false); trigger.current?.focus(); }}
          onConfirm={() => { setConfirming(false); onDelete(); }} />
      ) : null}
    </div>
  );
}

/** A destructive whole-page action behind an in-page confirm, as Access & privacy's Clear history. */
function DangerAction({ title, description, button, confirm, onConfirm, disabled }: {
  title: string; description: string; button: string; confirm: string; onConfirm: () => void; disabled?: boolean;
}) {
  const [confirming, setConfirming] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const refocus = useRef(false);
  useEffect(() => { if (!confirming && refocus.current) { refocus.current = false; trigger.current?.focus(); } }, [confirming]);
  return confirming ? (
    <ConfirmRow title={confirm} keepLabel="Cancel" confirmLabel={button}
      onKeep={() => { refocus.current = true; setConfirming(false); }}
      onConfirm={() => { setConfirming(false); onConfirm(); }} />
  ) : (
    <Row title={title} description={description} tone="danger">
      <PillButton variant="danger" ref={trigger} disabled={disabled} onClick={() => setConfirming(true)}>{button}</PillButton>
    </Row>
  );
}

// ---- Notes

export function NotesPage({ state }: { state: ShellState }) {
  const n = state.settings.notes;
  const [editing, setEditing] = useState<string | null>(null);
  const [title, setTitle] = useState("");
  const [text, setText] = useState("");
  const [query, setQuery] = useState("");
  const [saving, save] = useSave(n.items.map((x) => x.id + x.updatedAt).join());
  const titleField = useRef<HTMLInputElement>(null);
  const reset = () => { setEditing(null); setTitle(""); setText(""); };
  const edit = (note: Note) => { setEditing(note.id); setTitle(note.title); setText(note.text); titleField.current?.focus(); };
  const full = editing === null && n.items.length >= n.limit;
  const shown = n.items.filter((x) => matches(query, x.title, x.text));
  return (
    <Page title="Notes" subtitle="Plain-text notes kept only on this PC. AI providers don't read them unless you ask.">
      {n.error ? <Locked error={n.error} /> : null}
      <Group title={editing ? "Edit note" : "New note"}>
        <Field id="note-title" label="Title">
          <input id="note-title" ref={titleField} className="settings-input" maxLength={100} disabled={!n.editable}
            value={title} onChange={(e) => setTitle(e.target.value)} />
        </Field>
        <Field id="note-text" label="Note">
          <textarea id="note-text" className="settings-input settings-input--area" rows={5} maxLength={10_000} disabled={!n.editable}
            value={text} onChange={(e) => setText(e.target.value)} />
        </Field>
        <div className="settings-actions">
          {full ? <span className="settings-field__hint settings-actions__error">You have {n.limit} notes, the most there can be. Delete one to add another.</span> : null}
          {editing ? <PillButton onClick={reset}>Cancel</PillButton> : null}
          <PillButton variant="primary" busy={saving} disabled={!n.editable || full || title.trim().length === 0}
            onClick={() => save(() => act("note.save", JSON.stringify({ id: editing ?? "", title, text })), reset)}>
            {saving ? "Saving…" : editing ? "Save note" : "Add note"}
          </PillButton>
        </div>
      </Group>
      {n.items.length === 0 ? (
        n.editable ? <div className="settings-empty"><p>No notes yet. Write one above, or say “note:” followed by what to save.</p></div> : null
      ) : (
        <>
          <div className="settings-toolbar">
            <div className="settings-toolbar__search"><SearchField label="Search notes" placeholder="Search title or text" value={query} onChange={setQuery} /></div>
            <PillButton size="sm" onClick={() => act("note.export")}>Export as Markdown</PillButton>
          </div>
          <Group title={`${n.items.length} of ${n.limit} notes`}>
            {shown.length === 0 ? <p className="settings-empty settings-empty--inline">No notes match your search.</p> : shown.map((note) => (
              <Item key={note.id} name={note.title} confirm="Delete this note? This cannot be undone."
                onDelete={() => { if (editing === note.id) reset(); act("note.delete", note.id); }}
                row={<Row title={<span className="oc-ellipsis" title={note.title}>{note.title}</span>}
                  description={<><span className="settings-line oc-ellipsis">{oneLine(note.text) || "No text"}</span><span className="settings-line">Updated {shortDate(note.updatedAt)}</span></>} />}
                actions={<PillButton size="sm" aria-label={`Edit ${note.title}`} disabled={!n.editable} onClick={() => edit(note)}>Edit</PillButton>} />
            ))}
          </Group>
        </>
      )}
    </Page>
  );
}

// ---- Clipboard shelf

function ClipItem({ item, index, count, isNext }: { item: Clip; index: number; count: number; isNext: boolean }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(item.text);
  const [saving, save] = useSave(item.text);
  const name = oneLine(item.text).slice(0, 40) || "empty item";
  if (editing) {
    return (
      <div className="settings-item">
        <Field id={`clip-edit-${item.id}`} label="Edit item">
          <textarea id={`clip-edit-${item.id}`} className="settings-input settings-input--area" rows={4} maxLength={10_000} autoFocus
            value={draft} onChange={(e) => setDraft(e.target.value)} />
        </Field>
        <div className="settings-actions">
          <PillButton size="sm" onClick={() => setEditing(false)}>Cancel</PillButton>
          <PillButton size="sm" variant="primary" busy={saving} disabled={draft.trim().length === 0 || draft === item.text}
            onClick={() => save(() => act("clipboard.edit", JSON.stringify({ id: item.id, text: draft })), () => setEditing(false))}>
            {saving ? "Saving…" : "Save edit"}
          </PillButton>
        </div>
      </div>
    );
  }
  return (
    <Item name={name} onDelete={() => act("clipboard.delete", item.id)}
      row={(
        <Row title={<span className="oc-ellipsis" title={item.text}>{oneLine(item.text) || "Empty item"}</span>}
          description={`${item.source ?? "Source unknown"} · ${shortDate(item.addedAt)}${item.truncated ? " · longer than shown" : ""}`}>
          {item.pinned ? <Badge>Pinned</Badge> : null}
          {isNext ? <Badge tone="muted">Next</Badge> : null}
        </Row>
      )}
      actions={(
        <>
          <PillButton size="sm" aria-label={`Copy ${name}`} onClick={() => act("clipboard.copy", item.id)}>Copy</PillButton>
          <PillButton size="sm" aria-label={`${item.pinned ? "Unpin" : "Pin"} ${name}`} onClick={() => act(item.pinned ? "clipboard.unpin" : "clipboard.pin", item.id)}>
            {item.pinned ? "Unpin" : "Pin"}
          </PillButton>
          <PillButton size="sm" aria-label={`Move ${name} up`} disabled={index === 0} onClick={() => act("clipboard.moveUp", item.id)}>Up</PillButton>
          <PillButton size="sm" aria-label={`Move ${name} down`} disabled={index === count - 1} onClick={() => act("clipboard.moveDown", item.id)}>Down</PillButton>
          {item.truncated ? null : <PillButton size="sm" aria-label={`Edit ${name}`} onClick={() => { setDraft(item.text); setEditing(true); }}>Edit</PillButton>}
        </>
      )} />
  );
}

export function ClipboardPage({ state }: { state: ShellState }) {
  const c = state.settings.clipboard;
  const [query, setQuery] = useState("");
  const [draft, setDraft] = useState("");
  const [adding, add] = useSave(c.items.map((i) => i.id).join());
  const full = c.items.length >= c.limit;
  return (
    <Page title="Clipboard shelf" subtitle="Text you've collected to paste later. It stays on this PC and is not sent to AI.">
      {!c.available ? <Locked error={c.error ?? "Saved clipboard items could not be opened."} /> : (
        <>
          <Group>
            <Row title="Collect copied text" description="Text you copy in other apps is saved here until you delete it. Copies in protected windows are skipped.">
              <Toggle label="Collect copied text" checked={c.collecting} onChange={(on) => act("clipboard.collect", on ? "on" : "off")} />
            </Row>
            <Field id="clip-add" label="Add text">
              <textarea id="clip-add" className="settings-input settings-input--area" rows={3} maxLength={10_000}
                value={draft} onChange={(e) => setDraft(e.target.value)} />
            </Field>
            <div className="settings-actions">
              {full ? <span className="settings-field__hint settings-actions__error">The shelf is full. Delete an item to add more.</span> : null}
              <PillButton variant="primary" busy={adding} disabled={full || draft.trim().length === 0}
                onClick={() => add(() => act("clipboard.add", draft), () => setDraft(""))}>{adding ? "Adding…" : "Add to shelf"}</PillButton>
            </div>
          </Group>
          {c.items.length === 0 ? (
            <div className="settings-empty"><p>Nothing on the shelf yet. Turn on Collect copied text, or add text above.</p></div>
          ) : (
            <>
              <div className="settings-toolbar">
                <div className="settings-toolbar__search"><SearchField label="Search the shelf" placeholder="Search text or source" value={query} onChange={setQuery} /></div>
                <PillButton size="sm" onClick={() => act("clipboard.copyNext")}>Copy next</PillButton>
                <PillButton size="sm" onClick={() => act("clipboard.copyAll")}>Copy all</PillButton>
              </div>
              <Group title={`${c.items.length} of ${c.limit} items`}>
                {c.items.map((item, index) => (matches(query, item.text, item.source)
                  ? <ClipItem key={item.id} item={item} index={index} count={c.items.length} isNext={item.id === c.nextId} /> : null))}
                {c.items.some((i) => matches(query, i.text, i.source)) ? null : <p className="settings-empty settings-empty--inline">Nothing on the shelf matches your search.</p>}
              </Group>
              <Group>
                <DangerAction title="Clear the shelf" description="Deletes every item from this PC." button="Clear shelf"
                  confirm="Delete everything on the clipboard shelf? This cannot be undone." onConfirm={() => act("clipboard.clear")} />
              </Group>
            </>
          )}
        </>
      )}
      <p className="settings-note">Copy puts the text on the Windows clipboard: press Ctrl+V where you want it. To paste items one by one into a chosen field, use the clipboard shelf in the classic window.</p>
    </Page>
  );
}

// ---- Tasks

const RUN_STATUS: Record<string, { tone: "ok" | "warn" | "muted" | "danger"; text: string }> = {
  completed: { tone: "ok", text: "Done" },
  failed: { tone: "danger", text: "Failed" },
  stopped: { tone: "muted", text: "Stopped" },
  paused: { tone: "muted", text: "Paused" },
  "awaiting-approval": { tone: "warn", text: "Needs you" },
};
const runStatus = (status: string) => RUN_STATUS[status] ?? { tone: "warn" as const, text: "Working" };
const ACCESS: Record<string, string> = { Preview: "Preview only", ConfirmEach: "Approve each action", Auto: "Auto", FullAccess: "Full access" };

function TaskRun({ run }: { run: Run }) {
  const [open, setOpen] = useState(false);
  const status = runStatus(run.status);
  return (
    <div className="settings-item">
      <Row title={<span className="oc-ellipsis" title={run.task}>{run.task}</span>}
        description={`${ACCESS[run.access] ?? run.access} · ${shortDate(run.startedAt)}`}>
        <Badge tone={status.tone}>{status.text}</Badge>
      </Row>
      <div className="settings-actions">
        {run.events.length > 0 ? (
          <button type="button" className="settings-disclosure settings-actions__error" aria-expanded={open} onClick={() => setOpen((on) => !on)}>
            <ChevronIcon />{open ? "Hide steps" : `Show steps (${run.events.length})`}
          </button>
        ) : null}
        <PillButton size="sm" aria-label={`Export events of ${run.task}`} onClick={() => act("task.export", run.id)}>Export</PillButton>
      </div>
      {open ? (
        <ol className="settings-steps" aria-label={`Steps of ${run.task}`}>
          {run.events.map((e, i) => <li key={i}>{e.message || runStatus(e.status).text}</li>)}
        </ol>
      ) : null}
    </div>
  );
}

export function TasksPage({ state }: { state: ShellState }) {
  const t = state.settings.tasks;
  const now = t.paused ? "Paused. Resume waits 3 seconds so you can bring the task's window to the front." : t.active ? "Running." : "No task is running.";
  return (
    <Page title="Tasks" subtitle="Desktop tasks OpenClicky has run on this PC, step by step.">
      <Group>
        <Row title="Current task" description={now} />
        <div className="settings-actions">
          <PillButton size="sm" disabled={!t.active || t.paused} onClick={() => act("task.pause")}>Pause</PillButton>
          <PillButton size="sm" disabled={!t.paused} onClick={() => act("task.resume")}>Resume</PillButton>
          <PillButton size="sm" variant="danger" disabled={!t.active} onClick={() => act("task.stop")}>Stop</PillButton>
        </div>
      </Group>
      {t.runs.length === 0 ? (
        <div className="settings-empty"><p>No tasks yet. Ask OpenClicky to do something on your PC and its steps show here.</p></div>
      ) : (
        <>
          <Group title="History">{t.runs.map((run) => <TaskRun key={run.id} run={run} />)}</Group>
          <Group>
            <DangerAction title="Clear task history" description="Removes saved task text and progress." button="Clear history"
              confirm="Clear saved task text and progress? A running task is stopped." onConfirm={() => act("task.clear")} />
          </Group>
        </>
      )}
      <p className="settings-note">You can also say “pause task”, “resume task” or “stop task”.</p>
    </Page>
  );
}

// ---- Memory

function FactItem({ fact, editable }: { fact: Fact; editable: boolean }) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(fact.text);
  const [saving, save] = useSave(fact.text);
  const name = oneLine(fact.text).slice(0, 40);
  if (editing) {
    return (
      <div className="settings-item">
        <Field id={`fact-${fact.id}`} label="Edit memory">
          <textarea id={`fact-${fact.id}`} className="settings-input settings-input--area" rows={3} maxLength={1_000} autoFocus
            value={draft} onChange={(e) => setDraft(e.target.value)} />
        </Field>
        <div className="settings-actions">
          <PillButton size="sm" onClick={() => setEditing(false)}>Cancel</PillButton>
          <PillButton size="sm" variant="primary" busy={saving} disabled={draft.trim().length === 0 || draft.trim() === fact.text}
            onClick={() => save(() => act("memory.update", JSON.stringify({ id: fact.id, text: draft })), () => setEditing(false))}>
            {saving ? "Saving…" : "Save memory"}
          </PillButton>
        </div>
      </div>
    );
  }
  return (
    <Item name={name} confirm="Delete this memory? This cannot be undone." disabled={!editable} onDelete={() => act("memory.delete", fact.id)}
      row={<Row title={fact.text}><Badge tone={fact.learned ? "ok" : "muted"}>{fact.learned ? "Learned" : "Added by you"}</Badge></Row>}
      actions={<PillButton size="sm" aria-label={`Edit ${name}`} disabled={!editable} onClick={() => { setDraft(fact.text); setEditing(true); }}>Edit</PillButton>} />
  );
}

export function MemoryPage({ state }: { state: ShellState }) {
  const m = state.settings.memory;
  const [name, setName] = useDraft(m.name);
  const [personality, setPersonality] = useDraft(m.personality);
  const [instructions, setInstructions] = useDraft(m.instructions);
  const [fact, setFact] = useState("");
  const [savingProfile, saveProfile] = useSave([m.name, m.personality, m.instructions].join("\u0000"));
  const [adding, add] = useSave(m.facts.map((f) => f.id).join());
  const changed = name !== m.name || personality !== m.personality || instructions !== m.instructions;
  const full = m.facts.length >= m.limit;
  return (
    <Page title="Memory" subtitle="What OpenClicky remembers about you. Kept on this PC and sent with your future requests.">
      {m.error ? <Locked error={m.error} /> : null}
      <Group title="Companion profile">
        <Field id="memory-name" label="Name">
          <input id="memory-name" className="settings-input" maxLength={80} disabled={!m.editable} value={name} onChange={(e) => setName(e.target.value)} />
        </Field>
        <Field id="memory-personality" label="Personality">
          <textarea id="memory-personality" className="settings-input settings-input--area" rows={3} maxLength={4_000} disabled={!m.editable}
            value={personality} onChange={(e) => setPersonality(e.target.value)} />
        </Field>
        <Field id="memory-instructions" label="Instructions" hint="How OpenClicky should answer you, in every request.">
          <textarea id="memory-instructions" className="settings-input settings-input--area" rows={4} maxLength={8_000} disabled={!m.editable}
            value={instructions} onChange={(e) => setInstructions(e.target.value)} />
        </Field>
        <div className="settings-actions">
          <PillButton variant="primary" busy={savingProfile} disabled={!m.editable || !changed}
            onClick={() => saveProfile(() => act("memory.profile", JSON.stringify({ name, personality, instructions })))}>
            {savingProfile ? "Saving…" : "Save profile"}
          </PillButton>
        </div>
      </Group>
      <Group title={`Memories · ${m.facts.length} of ${m.limit}`}>
        <Field id="memory-add" label="Add a memory">
          <textarea id="memory-add" className="settings-input settings-input--area" rows={2} maxLength={1_000} disabled={!m.editable}
            value={fact} onChange={(e) => setFact(e.target.value)} />
        </Field>
        <div className="settings-actions">
          {full ? <span className="settings-field__hint settings-actions__error">That's the most memories there can be. Delete one to add another.</span> : null}
          <PillButton variant="primary" busy={adding} disabled={!m.editable || full || fact.trim().length === 0}
            onClick={() => add(() => act("memory.add", fact), () => setFact(""))}>{adding ? "Adding…" : "Add memory"}</PillButton>
        </div>
        {m.facts.length === 0
          ? (m.editable ? <p className="settings-empty settings-empty--inline">No memories yet. Add one above, or say “remember:” followed by what to keep.</p> : null)
          : m.facts.map((f) => <FactItem key={f.id} fact={f} editable={m.editable} />)}
      </Group>
      <Group title="Saved memory">
        <Row title="Export memories" description="Save a copy as a JSON file to read or back up.">
          <PillButton disabled={!m.saved} onClick={() => act("exportMemory")}>Export</PillButton>
        </Row>
        <DangerAction title="Clear all memory" description="The profile and every memory. Export first if you need a copy." button="Clear memory"
          disabled={!m.saved} confirm="Clear the profile and every memory? This cannot be undone." onConfirm={() => act("memory.clear")} />
      </Group>
    </Page>
  );
}
