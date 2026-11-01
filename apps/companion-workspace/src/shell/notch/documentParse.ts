/** Pure parsing for the Document tab: a minimal markdown block parser, CSV and column statistics. */

export type MdBlock =
  | { t: "h"; level: 1 | 2 | 3; text: string }
  | { t: "p"; text: string }
  | { t: "ul" | "ol"; items: string[] }
  | { t: "code"; text: string };

/** Headings (#-###), paragraphs, "-"/"1." lists and fenced code. Everything else is paragraph text. */
export function parseMarkdown(source: string): MdBlock[] {
  const blocks: MdBlock[] = [];
  let para: string[] = [];
  let list = null as { t: "ul" | "ol"; items: string[] } | null; // cast: TS does not see flush() reassign it
  const flush = () => {
    if (para.length) blocks.push({ t: "p", text: para.join(" ") });
    if (list) blocks.push(list);
    para = []; list = null;
  };
  const lines = source.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (/^\s*```/.test(line)) {
      flush();
      const code: string[] = [];
      for (i++; i < lines.length && !/^\s*```/.test(lines[i]); i++) code.push(lines[i]);
      blocks.push({ t: "code", text: code.join("\n") });
      continue;
    }
    const heading = /^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$/.exec(line);
    const item = /^\s*(?:([-*+])|\d+[.)])\s+(.*)$/.exec(line);
    if (!line.trim()) flush();
    else if (heading) { flush(); blocks.push({ t: "h", level: Math.min(heading[1].length, 3) as 1 | 2 | 3, text: heading[2] }); }
    else if (item) {
      const t = item[1] ? "ul" : "ol";
      if (list?.t !== t) { flush(); list = { t, items: [] }; }
      list!.items.push(item[2]);
    } else { if (list) flush(); para.push(line.trim()); }
  }
  flush();
  return blocks;
}

export type Inline = { t: "text" | "code" | "bold"; text: string } | { t: "link"; text: string; url: string };

// ponytail: closers are looked for within a bounded window, so a paragraph of unmatched "[" or "**" stays linear.
// Code spans, link text and bold longer than MAX_SPAN, or urls longer than MAX_URL, simply render as plain text.
const MAX_SPAN = 300;
const MAX_URL = 2_048;

/** Index of `needle` within `max` characters after `from`, or -1. Slices first so a missing needle never scans the whole text. */
function within(text: string, needle: string, from: number, max: number, last: number): number {
  if (last < from) return -1; // no closer anywhere after here (last = text.lastIndexOf(needle))
  const at = text.slice(from, from + max + needle.length).indexOf(needle);
  return at < 0 ? -1 : from + at;
}

/** `code`, **bold** and [text](url) in one left-to-right pass. Links stay plain text: the caller shows the url in a title, never an anchor. */
export function parseInline(text: string): Inline[] {
  const out: Inline[] = [];
  const last = { tick: text.lastIndexOf("`"), square: text.lastIndexOf("]"), paren: text.lastIndexOf(")"), bold: text.lastIndexOf("**") };
  let plain = 0; // start of the pending plain text
  let i = 0;
  while (i < text.length) {
    let token: Inline | null = null;
    let end = i;
    const ch = text[i];
    if (ch === "`") {
      const close = within(text, "`", i + 1, MAX_SPAN, last.tick);
      if (close > i + 1 && !text.slice(i + 1, close).includes("\n")) { token = { t: "code", text: text.slice(i + 1, close) }; end = close + 1; }
    } else if (ch === "[") {
      const close = within(text, "]", i + 1, MAX_SPAN, last.square);
      if (close > i + 1 && text[close + 1] === "(") {
        const stop = within(text, ")", close + 2, MAX_URL, last.paren);
        const url = stop > close + 2 ? text.slice(close + 2, stop) : "";
        const label = text.slice(i + 1, close);
        if (url && !/\s/.test(url) && !label.includes("\n")) { token = { t: "link", text: label, url }; end = stop + 1; }
      }
    } else if (ch === "*" && text[i + 1] === "*") {
      const close = within(text, "**", i + 2, MAX_SPAN, last.bold);
      const body = close > i + 2 ? text.slice(i + 2, close) : "";
      if (body && !body.includes("*") && !body.includes("\n")) { token = { t: "bold", text: body }; end = close + 2; }
    }
    if (!token) { i++; continue; }
    if (plain < i) out.push({ t: "text", text: text.slice(plain, i) });
    out.push(token);
    i = plain = end;
  }
  if (plain < text.length) out.push({ t: "text", text: text.slice(plain) });
  return out;
}

/** Comma separated; double quotes may hold commas, newlines and "" for a quote. Blank lines are skipped. */
export function parseCsv(source: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let quoted = false;
  const endRow = () => {
    row.push(cell); cell = "";
    if (row.length > 1 || row[0] !== "") rows.push(row);
    row = [];
  };
  for (let i = 0; i < source.length; i++) {
    const ch = source[i];
    if (quoted) {
      if (ch !== '"') cell += ch;
      else if (source[i + 1] === '"') { cell += '"'; i++; }
      else quoted = false;
    } else if (ch === '"') quoted = true;
    else if (ch === ",") { row.push(cell); cell = ""; }
    else if (ch === "\n" || ch === "\r") { if (ch === "\r" && source[i + 1] === "\n") i++; endRow(); }
    else cell += ch;
  }
  if (cell !== "" || row.length) endRow();
  return rows;
}

/** A cell as a number, allowing thousands commas and a leading "$" or trailing "%"; null when it is not one. */
export function parseNumber(cell: string): number | null {
  const s = cell.trim().replace(/[,$%\s]/g, "");
  return /^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(s) ? Number(s) : null;
}

/** A column is numeric when it has at least one number and no other non-empty cell. */
export function isNumericColumn(body: string[][], col: number): boolean {
  let numbers = 0;
  for (const row of body) {
    const cell = (row[col] ?? "").trim();
    if (!cell) continue;
    if (parseNumber(cell) === null) return false;
    numbers++;
  }
  return numbers > 0;
}

export function columnStats(body: string[][], col: number): { sum: number; average: number; count: number } {
  let sum = 0, count = 0;
  for (const row of body) {
    const n = parseNumber(row[col] ?? "");
    if (n !== null) { sum += n; count++; }
  }
  return { sum, average: count ? sum / count : 0, count };
}

const formatter = new Intl.NumberFormat("en-US", { maximumFractionDigits: 4 });
export const formatNumber = (n: number): string => formatter.format(n);
