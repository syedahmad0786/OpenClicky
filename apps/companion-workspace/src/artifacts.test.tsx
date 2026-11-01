import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ArtifactView } from "./artifacts";

describe("interactive artifacts", () => {
  it("recomputes calculator values through a validated edit callback", () => {
    const onChange = vi.fn();
    render(<ArtifactView artifact={{ kind: "calculator", id: "calc", title: "Estimate", fields: [{ id: "build", label: "Build", value: 4, min: 0, max: 10, step: 1 }] }} onChange={onChange} />);
    fireEvent.change(screen.getByRole("slider"), { target: { value: "7" } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ fields: [expect.objectContaining({ value: 7 })] }));
  });

  it("selects connected diagram nodes", () => {
    render(<ArtifactView artifact={{ kind: "diagram", id: "flow", title: "Flow", nodes: [{ id: "one", label: "One", detail: "First" }, { id: "two", label: "Two", detail: "Second" }], edges: [{ from: "one", to: "two" }] }} onChange={() => {}} />);
    fireEvent.click(screen.getByRole("button", { name: "Two" }));
    expect(screen.getByText("Second")).toBeVisible();
  });

  it("draws only declared branching edges and leaves disconnected nodes unlinked", () => {
    const { container } = render(<ArtifactView artifact={{ kind: "diagram", id: "flow", title: "Branch", nodes: [
      { id: "start", label: "Start", detail: "Root" }, { id: "left", label: "Left", detail: "Branch one" },
      { id: "right", label: "Right", detail: "Branch two" }, { id: "alone", label: "Alone", detail: "Disconnected" },
    ], edges: [{ from: "start", to: "left" }, { from: "start", to: "right" }] }} onChange={() => {}} />);
    expect(container.querySelectorAll("[data-edge]")).toHaveLength(2);
    expect(container.querySelector('[data-edge="start-left"]')).toBeInTheDocument();
    expect(container.querySelector('[data-edge="start-right"]')).toBeInTheDocument();
    expect(container.querySelector('[data-edge*="alone"]')).not.toBeInTheDocument();
    screen.getAllByRole("button").forEach((button) => expect(button).toHaveAttribute("type", "button"));
  });

  it("renders a six-face cube in an isolated frame and updates rotation", () => {
    const onChange = vi.fn();
    const view = render(<ArtifactView artifact={{ kind: "scene3d", id: "cube", title: "Cube", shape: "cube", color: "#25664c", rotation: 24 }} onChange={onChange} />);
    const frame = view.getByTitle("cube preview");
    expect(frame).toHaveAttribute("sandbox", "");
    expect(frame.getAttribute("srcdoc")?.match(/<i class="cube-face/g)).toHaveLength(6);
    expect(frame.getAttribute("srcdoc")).toContain("rotateY(24deg)");
    fireEvent.change(view.container.querySelector('input[type="range"]')!, { target: { value: "75" } });
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ rotation: 75 }));
  });
});
