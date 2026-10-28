import { expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { ACCENTS } from "./ui/accents";

// Reads the real tokens out of theme.css (both themes, every accent) and checks WCAG contrast for the pairs the UI draws.
const css = readFileSync(resolve(__dirname, "theme.css"), "utf8").replace(/\r\n/g, "\n"); // a CRLF checkout reads the same

type Rgba = [number, number, number, number];
type Tokens = Record<string, string>;

function block(selector: string): Tokens {
  const start = css.indexOf(`${selector} {`);
  if (start < 0) throw new Error(`no block ${selector}`);
  const body = css.slice(start + selector.length + 2, css.indexOf("}", start));
  return Object.fromEntries([...body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)].map((m) => [m[1], m[2].trim()]));
}

const base = block(":root");
const dark = { ...base, ...block(':root,\n:root[data-theme="dark"]') };
const lightOnly = block(':root[data-theme="light"]');
const light = { ...dark, ...lightOnly };
const fallback = block("  :root:not([data-theme])");

function parse(value: string, t: Tokens): Rgba {
  value = value.trim();
  const ref = /^var\((--[\w-]+)\)$/.exec(value);
  if (ref) return parse(t[ref[1]], t);
  if (value === "white") return [255, 255, 255, 1];
  if (value === "black") return [0, 0, 0, 1];
  if (value === "transparent") return [0, 0, 0, 0];
  const hex = /^#([0-9a-f]{6})$/i.exec(value);
  if (hex) return [0, 2, 4].map((i) => parseInt(hex[1].slice(i, i + 2), 16)).concat(1) as Rgba;
  const rgb = /^rgb\((\d+) (\d+) (\d+)(?: \/ ([\d.]+))?\)$/.exec(value);
  if (rgb) return [+rgb[1], +rgb[2], +rgb[3], rgb[4] === undefined ? 1 : +rgb[4]];
  const mix = /^color-mix\(in srgb, (.+) (\d+)%, (.+)\)$/.exec(value);
  if (mix) {
    const a = parse(mix[1], t), b = parse(mix[3], t), p = +mix[2] / 100;
    const alpha = a[3] * p + b[3] * (1 - p);
    // srgb mixing with premultiplied alpha, as CSS Color 5 specifies.
    const ch = (i: number) => (alpha ? (a[i] * a[3] * p + b[i] * b[3] * (1 - p)) / alpha : 0);
    return [ch(0), ch(1), ch(2), alpha];
  }
  throw new Error(`cannot parse ${value}`);
}

/** Colour `top` drawn over the opaque `under`. */
const over = (top: Rgba, under: Rgba): Rgba => [0, 1, 2].map((i) => top[i] * top[3] + under[i] * (1 - top[3])).concat(1) as Rgba;
const luminance = ([r, g, b]: Rgba) => {
  const lin = (c: number) => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
};
const ratio = (a: Rgba, b: Rgba) => { const [x, y] = [luminance(a), luminance(b)].sort((m, n) => n - m); return (x + 0.05) / (y + 0.05); };

/** The paper stocks (oc-paper--*), the same in both themes. */
const PAPER = ["#f3efe2", "#f7e9a8", "#cfeedd", "#cfe3f7", "#fbd9c2", "#f8d3dc"];
/** The deck's "Needs you" colour, read from deck.css. */
const DECK_WARN = /data-tone="warn"\] \{ color: (#[0-9a-f]{6})/i.exec(readFileSync(resolve(__dirname, "deck/deck.css"), "utf8"))![1];

/** [foreground, background, minimum] with token names, or a raw colour, or [colour, over] for a translucent background. */
type Bg = string | [string, string];
const PAIRS: [string, Bg, number][] = [
  ["--text", "--panel", 4.5], ["--text", "--card", 4.5],
  ["--text-2", "--panel", 4.5], ["--text-2", "--card", 4.5], ["--text-2", "--card-hover", 4.5], ["--text-2", "--field", 4.5],
  ["--text-3", "--panel", 4.5], ["--text-3", "--card", 4.5], ["--text-3", "--field", 4.5],
  ["--danger", "--card", 4.5], ["--danger", "--panel", 4.5],
  ["--success", ["--success-bg", "--card"], 4.5],
  ["--warn", ["--warn-bg", "--card"], 4.5],
  ["--danger", ["color-mix(in srgb, var(--danger) 14%, transparent)", "--card"], 4.5],
  ["--text-2", "--field", 4.5], // muted badge
  // Controls: the off knob against its track, and the track against the card it sits on.
  ["--knob-off", "--track-off", 3], ["--track-off", "--card", 3],
  // The deck's "Needs you" line (deck.css) on every paper stock.
  ...PAPER.map((paper): [string, Bg, number] => [DECK_WARN, paper, 4.5]),
];
const ACCENT_PAIRS: [string, Bg, number][] = [
  ["--accent-text", "--panel", 4.5], ["--accent-text", "--card", 4.5], ["--accent-text", "--field", 4.5],
  ["--on-accent", "--accent", 4.5], ["--on-accent", "--accent-light", 4.5],
  ["--on-tint", ["--accent-tint", "--field"], 4.5], ["--on-tint", ["--accent-tint", "--panel"], 4.5],
  // The switched-on toggle: its track against the card, and the knob against the track.
  ["--toggle-on", "--card", 3], ["--toggle-on-knob", "--toggle-on", 3],
  // Accent text on paper cards (the deck's "Ready to view"), which are light in both themes.
  ...PAPER.map((paper): [string, Bg, number] => ["--accent-ink", paper, 4.5]),
];

const colour = (name: string, t: Tokens) => parse(name.startsWith("--") ? `var(${name})` : name, t);
const background = (bg: Bg, t: Tokens) => (typeof bg === "string" ? colour(bg, t) : over(colour(bg[0], t), colour(bg[1], t)));

it.each([["dark", dark], ["light", light]] as const)("%s theme: text and control pairs meet WCAG contrast", (_, tokens) => {
  const failures: string[] = [];
  const check = (t: Tokens, label: string, [fg, bg, min]: [string, Bg, number]) => {
    const value = ratio(colour(fg, t), background(bg, t));
    if (value < min) failures.push(`${label}${fg} on ${JSON.stringify(bg)}: ${value.toFixed(2)} < ${min}`);
  };
  for (const pair of PAIRS) check(tokens, "", pair);
  for (const { name, hex } of ACCENTS) for (const pair of ACCENT_PAIRS) check({ ...tokens, "--accent": hex }, `${name} `, pair);
  expect(failures).toEqual([]);
});

it("the system-light fallback repeats the light theme exactly", () => {
  expect(fallback).toEqual(lightOnly);
});

it("every state has a token in both themes, and the light theme overrides each colour that would fail on white", () => {
  for (const name of ["--accent-text", "--success", "--warn", "--danger", "--text-3", "--track-off"]) expect(lightOnly[name], name).toBeDefined();
  for (const name of ["--accent-text", "--text-3", "--track-off"]) expect(dark[name], name).toBeDefined();
});

it("reduced motion: a media query and the host switch both stop movement", () => {
  const media = /@media \(prefers-reduced-motion: reduce\) \{([\s\S]*?)\n\}/.exec(css)?.[1] ?? "";
  for (const rules of [media, css.slice(css.indexOf(':root[data-reduced-motion="true"]'))]) {
    expect(rules).toContain("transition-duration: 120ms !important");
    expect(rules).toMatch(/transition-property: opacity, color, background-color, border-color, box-shadow !important/);
    expect(rules).toMatch(/\.notch-card__dots i[^{]*\) \{ animation: none !important; \}/);
  }
});

it("motion and rhythm tokens exist, and no outline is removed without a replacement", () => {
  for (const [name, value] of [["--dur-fast", "120ms"], ["--dur", "160ms"], ["--dur-slow", "240ms"], ["--space-1", "4px"], ["--space-6", "32px"],
    ["--icon-sm", "14px"], ["--icon-md", "16px"], ["--icon-lg", "20px"]]) expect(base[name], name).toBe(value);
  expect(css).toMatch(/:where\(button, a\[href\], input, select, textarea, summary, \[tabindex\]\):focus-visible \{ outline: var\(--focus-ring\)/);
  const shell = resolve(__dirname);
  for (const file of ["notch/notch.css", "notch/document.css", "settings/settings.css", "cards/cards.css", "deck/deck.css", "onboarding/onboarding.css"]) {
    expect(readFileSync(resolve(shell, file), "utf8"), file).not.toMatch(/outline:\s*(0|none)\s*;/);
  }
});
