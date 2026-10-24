import { useState } from "react";
import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { KeyCaps, PaperCard, PillButton, RadioList, Segmented, Tile, Toggle, Meter, Row, Group, SectionHeader, SearchField } from "./index";

afterEach(cleanup);

it("Toggle flips aria-checked and calls onChange", () => {
  const onChange = vi.fn();
  function Host() {
    const [on, setOn] = useState(false);
    return <Toggle label="Launch at login" checked={on} onChange={(v) => { onChange(v); setOn(v); }} />;
  }
  render(<Host />);
  const toggle = screen.getByRole("switch", { name: "Launch at login" });
  expect(toggle).toHaveAttribute("aria-checked", "false");
  fireEvent.click(toggle);
  expect(onChange).toHaveBeenLastCalledWith(true);
  expect(toggle).toHaveAttribute("aria-checked", "true");
  fireEvent.click(toggle);
  expect(onChange).toHaveBeenLastCalledWith(false);
  expect(toggle).toHaveAttribute("aria-checked", "false");
});

it("Segmented arrow keys move selection (wrapping) and keep a roving tab stop", () => {
  const onChange = vi.fn();
  function Host() {
    const [v, setV] = useState("a");
    return <Segmented label="Speed" value={v} onChange={(x) => { onChange(x); setV(x); }}
      options={[{ value: "a", label: "A" }, { value: "b", label: "B" }, { value: "c", label: "C" }]} />;
  }
  render(<Host />);
  expect(screen.getByRole("radiogroup", { name: "Speed" })).toBeInTheDocument();
  const [a, b, c] = screen.getAllByRole("radio");
  expect(a).toHaveAttribute("aria-checked", "true");
  expect(a).toHaveAttribute("tabindex", "0");
  expect(b).toHaveAttribute("tabindex", "-1");
  fireEvent.keyDown(a, { key: "ArrowRight" });
  expect(onChange).toHaveBeenLastCalledWith("b");
  expect(b).toHaveAttribute("aria-checked", "true");
  expect(b).toHaveFocus();
  fireEvent.keyDown(b, { key: "ArrowRight" });
  fireEvent.keyDown(c, { key: "ArrowRight" });
  expect(onChange).toHaveBeenLastCalledWith("a");
  fireEvent.keyDown(a, { key: "ArrowLeft" });
  expect(onChange).toHaveBeenLastCalledWith("c");
});

it("KeyCaps renders one cap per key", () => {
  const { container } = render(<KeyCaps keys={["Ctrl", "Alt"]} />);
  expect(container.querySelectorAll("kbd")).toHaveLength(2);
  expect(screen.getByText("Ctrl")).toBeInTheDocument();
  expect(screen.getByText("Alt")).toBeInTheDocument();
});

it("PillButton primary is wired to --accent in the theme", () => {
  render(<PillButton variant="primary">Open</PillButton>);
  const button = screen.getByRole("button", { name: "Open" });
  expect(button).toHaveClass("oc-pill", "oc-pill--primary");
  expect(button).toHaveAttribute("type", "button");
  const css = readFileSync(resolve(__dirname, "../theme.css"), "utf8");
  const rule = /\.oc-pill--primary\s*\{[^}]*\}/.exec(css)?.[0] ?? "";
  expect(rule).toContain("var(--accent");
  for (const accent of ["#3380FF", "#FF4C4C", "#D4A017", "#26C281"]) expect(css.toUpperCase()).toContain(accent.toUpperCase());
});

it("Tile is a toggle button with a check badge only when selected", () => {
  const onClick = vi.fn();
  const { rerender } = render(<Tile name="Blue" descriptor="Default" selected={false} onClick={onClick} />);
  const tile = screen.getByRole("button", { name: /Blue/ });
  expect(tile).toHaveAttribute("aria-pressed", "false");
  expect(tile.querySelector(".oc-tile__check")).toBeNull();
  fireEvent.click(tile);
  expect(onClick).toHaveBeenCalledTimes(1);
  rerender(<Tile name="Blue" descriptor="Default" selected onClick={onClick} />);
  expect(screen.getByRole("button", { name: /Blue/ })).toHaveAttribute("aria-pressed", "true");
  expect(tile.querySelector(".oc-tile__check")).not.toBeNull();
});

it("RadioList renders native radios, honours disabled, and reports the choice", () => {
  const onChange = vi.fn();
  render(<RadioList name="mic" label="Input device" value="def" onChange={onChange}
    options={[{ value: "def", label: "System default" }, { value: "usb", label: "USB mic" }, { value: "gone", label: "Unplugged", disabled: true }]} />);
  expect(screen.getByRole("radiogroup", { name: "Input device" })).toBeInTheDocument();
  expect(screen.getByRole("radio", { name: "System default" })).toBeChecked();
  expect(screen.getByRole("radio", { name: "Unplugged" })).toBeDisabled();
  fireEvent.click(screen.getByRole("radio", { name: "USB mic" }));
  expect(onChange).toHaveBeenCalledWith("usb");
});

it("PaperCard applies stock and tilt, and shows a handle and title", () => {
  const { container } = render(<PaperCard stock="mint" tilt={-2.5} title="Weekly recap">body</PaperCard>);
  const card = container.firstElementChild as HTMLElement;
  expect(card).toHaveClass("oc-paper", "oc-paper--mint");
  expect(card.style.getPropertyValue("--tilt")).toBe("-2.5deg");
  expect(card.querySelector(".oc-paper__handle")).not.toBeNull();
  expect(screen.getByText("Weekly recap")).toBeInTheDocument();
});

it("Meter exposes its value, SearchField reports text, Group/Row/SectionHeader compose", () => {
  const onChange = vi.fn();
  render(<>
    <Meter label="Agent messages" value={6} max={150} />
    <SearchField label="Find language" value="" onChange={onChange} />
    <Group title="Behavior"><Row title="Pause" description="Stop listening"><span>x</span></Row></Group>
    <SectionHeader>Updates</SectionHeader>
  </>);
  const meter = screen.getByRole("meter", { name: "Agent messages" });
  expect(meter).toHaveAttribute("aria-valuenow", "6");
  expect(meter).toHaveAttribute("aria-valuemax", "150");
  fireEvent.change(screen.getByRole("searchbox", { name: "Find language" }), { target: { value: "fr" } });
  expect(onChange).toHaveBeenCalledWith("fr");
  expect(screen.getByText("Behavior")).toBeInTheDocument();
  expect(screen.getByText("Stop listening")).toBeInTheDocument();
  expect(screen.getByText("Updates")).toBeInTheDocument();
});
