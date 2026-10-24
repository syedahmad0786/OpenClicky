import { describe, expect, it } from "vitest";
import { artifactSchema, parseHostMessage } from "./contracts";

describe("workspace trust boundary", () => {
  it("accepts a bounded known artifact", () => {
    expect(artifactSchema.safeParse({ kind: "chart", id: "c", title: "Capacity", series: [{ label: "Build", value: 3 }] }).success).toBe(true);
  });

  it("rejects executable and unknown fields", () => {
    expect(artifactSchema.safeParse({ kind: "chart", id: "c", title: "Unsafe", series: [{ label: "x", value: 1 }], html: "<script>fetch('https://bad')</script>" }).success).toBe(false);
    expect(parseHostMessage({ kind: "session.snapshot", sessionId: "s", artifacts: [], token: "secret" })).toBeNull();
  });

  it("rejects broken diagrams and invalid calculator ranges", () => {
    expect(artifactSchema.safeParse({ kind: "diagram", id: "d", title: "Broken", nodes: [{ id: "one", label: "One", detail: "" }], edges: [{ from: "one", to: "missing" }] }).success).toBe(false);
    expect(artifactSchema.safeParse({ kind: "calculator", id: "k", title: "Broken", fields: [{ id: "x", label: "X", value: 8, min: 0, max: 5, step: 1 }] }).success).toBe(false);
  });
});
