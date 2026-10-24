import { useEffect, useState } from "react";
import type { ShellState } from "../contract";
import { CloseIcon, Group, PillButton, Row, SearchField, Tile, Toggle } from "../ui";
import { Page, patch } from "./common";

const MAX_WORDS = 50;
const MAX_WORD_LENGTH = 40;

function Dictionary({ words: hostWords }: { words: readonly string[] }) {
  // Optimistic copy so two quick adds build on each other; it follows the host whenever the host's list changes.
  const [words, setWords] = useState(hostWords);
  const signature = hostWords.join("\u0000");
  useEffect(() => setWords(hostWords), [signature]); // eslint-disable-line react-hooks/exhaustive-deps
  const post = (next: string[]) => { setWords(next); patch("dictation.dictionary", next); };
  const [draft, setDraft] = useState("");
  const [note, setNote] = useState("");
  const full = words.length >= MAX_WORDS;
  const add = () => {
    const word = draft.trim().slice(0, MAX_WORD_LENGTH);
    if (!word || full) return;
    if (words.some((w) => w.toLowerCase() === word.toLowerCase())) { setNote("Already in your dictionary."); return; }
    post([...words, word]);
    setDraft(""); setNote("");
  };
  return (
    <Group title="Dictionary">
      <div className="settings-add">
        <input className="settings-input" aria-label="Add a word" placeholder="A name or term to get right" maxLength={MAX_WORD_LENGTH} value={draft}
          onChange={(event) => { setDraft(event.target.value); setNote(""); }}
          onKeyDown={(event) => { if (event.key === "Enter") { event.preventDefault(); add(); } }} />
        <PillButton variant="primary" disabled={full || !draft.trim()} onClick={add}>Add</PillButton>
      </div>
      {note ? <p className="settings-field__error" style={{ margin: 0, padding: "0 16px 8px" }}>{note}</p> : null}
      {words.length ? (
        <div className="settings-words">
          {words.map((word) => (
            <span key={word} className="settings-word">{word}
              <button type="button" aria-label={`Remove ${word}`} onClick={() => post(words.filter((w) => w !== word))}><CloseIcon width={12} height={12} /></button>
            </span>
          ))}
        </div>
      ) : null}
      <div className="settings-count">{`${words.length} of ${MAX_WORDS} words`}</div>
    </Group>
  );
}

export function DictationPage({ state }: { state: ShellState }) {
  const d = state.settings.dictation;
  const [query, setQuery] = useState("");
  const q = query.trim().toLowerCase();
  const shown = q ? d.languages.filter((l) => [l.name, l.native, l.code].some((t) => t.toLowerCase().includes(q))) : d.languages;
  return (
    <Page title="Dictation" subtitle="Languages and words OpenClicky should get right.">
      <Group title="Language">
        <Row title="Auto-detect language" description="Work out the language you are speaking each time.">
          <Toggle label="Auto-detect language" checked={d.autoDetect} onChange={(value) => patch("dictation.autoDetect", value)} />
        </Row>
      </Group>
      <Group title="Languages" className="settings-tiles">
        <div className="settings-search"><SearchField label="Search languages" value={query} onChange={setQuery} disabled={d.autoDetect} /></div>
        <div className="settings-grid settings-grid--3" role="group" aria-label="Languages">
          {shown.map((l) => (
            <Tile key={l.code} name={l.name} descriptor={l.native === l.name ? undefined : l.native} selected={l.code === d.language}
              disabled={d.autoDetect} onClick={() => patch("dictation.language", l.code)} />
          ))}
        </div>
      </Group>
      <Dictionary words={d.dictionary} />
    </Page>
  );
}
