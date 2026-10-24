import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import type { DocumentView } from "../contract";
import { DocumentTab } from "./DocumentTab";
import { parseCsv, parseInline } from "./documentParse";

afterEach(() => { cleanup(); vi.restoreAllMocks(); delete window.chrome; });

function mockHost() {
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return { posts: (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind) };
}
const doc = (patch: Partial<DocumentView>): DocumentView => ({ path: "C:\\docs\\a.md", name: "a.md", kind: "markdown", content: "", truncated: false, ...patch });

it("header buttons post file.open and file.reveal", () => {
  const host = mockHost();
  render(<DocumentTab doc={doc({ content: "hi" })} onClose={() => {}} />);
  fireEvent.click(screen.getByRole("button", { name: "Open in its app" }));
  fireEvent.click(screen.getByRole("button", { name: "Show in folder" }));
  expect(host.posts("file.open")).toEqual([{ kind: "file.open", path: "C:\\docs\\a.md" }]);
  expect(host.posts("file.reveal")).toEqual([{ kind: "file.reveal", path: "C:\\docs\\a.md" }]);
});

it("close calls onClose", () => {
  mockHost();
  const onClose = vi.fn();
  render(<DocumentTab doc={doc({})} onClose={onClose} />);
  fireEvent.click(screen.getByRole("button", { name: "Close" }));
  expect(onClose).toHaveBeenCalled();
});

it("renders markdown headings, lists, code and links as text with a title", () => {
  mockHost();
  const content = "# Big\n## Mid\n### Small\n\nSome `code` and [a link](https://example.com/x) here.\n\n- one\n- two\n\n1. first\n2. second\n\n```\nlet a = 1;\n<b>raw</b>\n```";
  const { container } = render(<DocumentTab doc={doc({ content })} onClose={() => {}} />);
  expect(screen.getByRole("heading", { name: "Big" })).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Mid" })).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Small" })).toBeInTheDocument();
  expect(container.querySelectorAll("ul li")).toHaveLength(2);
  expect(container.querySelectorAll("ol li")).toHaveLength(2);
  expect(container.querySelector("p code")?.textContent).toBe("code");
  expect(container.querySelector("pre code")?.textContent).toBe("let a = 1;\n<b>raw</b>");
  expect(container.querySelector("a")).toBeNull();
  expect(screen.getByText("a link")).toHaveAttribute("title", "https://example.com/x");
});

it("never turns markdown into live HTML", () => {
  mockHost();
  const content = "<script>window.hacked = 1</script>\n\n<img src=x onerror=\"window.hacked = 2\">\n\n[x](javascript:alert(1)) **<i>bold</i>**";
  const { container } = render(<DocumentTab doc={doc({ content })} onClose={() => {}} />);
  expect(container.querySelector("script")).toBeNull();
  expect(container.querySelector("img")).toBeNull();
  expect(container.querySelector("i")).toBeNull();
  expect(container.querySelector("a")).toBeNull();
  expect(container.textContent).toContain("<script>window.hacked = 1</script>");
});

it("shows plain text in a pre", () => {
  mockHost();
  const { container } = render(<DocumentTab doc={doc({ kind: "text", content: "line 1\nline 2" })} onClose={() => {}} />);
  expect(container.querySelector("pre")?.textContent).toBe("line 1\nline 2");
});

it("pdf shows only the header and a message", () => {
  mockHost();
  const { container } = render(<DocumentTab doc={doc({ kind: "pdf", name: "r.pdf", content: "ignored" })} onClose={() => {}} />);
  expect(screen.getByText("Preview isn’t available for PDFs yet.")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Open in its app" })).toBeInTheDocument();
  expect(container.textContent).not.toContain("ignored");
});

it("says when only the first part of a file is shown", () => {
  mockHost();
  render(<DocumentTab doc={doc({ content: "x", truncated: true })} onClose={() => {}} />);
  expect(screen.getByText("Showing the first part of this file.")).toBeInTheDocument();
});

it("explains when no document is open", () => {
  mockHost();
  render(<DocumentTab doc={null} onClose={() => {}} />);
  expect(screen.getByText("No document is open.")).toBeInTheDocument();
});

it("parses quoted CSV fields, escaped quotes and embedded newlines", () => {
  expect(parseCsv('a,b\r\n"x, y","say ""hi"""\n"two\nlines",3\n')).toEqual([["a", "b"], ["x, y", 'say "hi"'], ["two\nlines", "3"]]);
});

it("csv renders a table and computes sum, average and count for the selected numeric column", () => {
  mockHost();
  const content = 'item,price,note\nPen,"1,200",ok\nCap,3.5,\nBag,,x';
  render(<DocumentTab doc={doc({ kind: "csv", name: "p.csv", content })} onClose={() => {}} />);
  expect(screen.getAllByRole("columnheader").map((h) => h.textContent)).toEqual(["item", "price", "note"]);
  expect(screen.getAllByRole("row")).toHaveLength(4);
  expect(screen.queryByRole("button", { name: "item" })).toBeNull(); // text column is not selectable
  expect(screen.queryByText("Sum")).toBeNull();

  fireEvent.click(screen.getByRole("button", { name: "price" }));
  const footer = screen.getByRole("group", { name: "Column statistics" });
  expect(within(footer).getByText("Sum").nextSibling?.textContent).toBe("1,203.5");
  expect(within(footer).getByText("Average").nextSibling?.textContent).toBe("601.75");
  expect(within(footer).getByText("Count").nextSibling?.textContent).toBe("2");

  fireEvent.click(screen.getByRole("button", { name: "price" })); // toggles off
  expect(screen.queryByRole("group", { name: "Column statistics" })).toBeNull();
});

it("inline parsing keeps its meaning: code, bold, link, plain text, unmatched markers", () => {
  expect(parseInline("a `b` **c** [d](http://x/y) e [f **g")).toEqual([
    { t: "text", text: "a " }, { t: "code", text: "b" }, { t: "text", text: " " }, { t: "bold", text: "c" }, { t: "text", text: " " },
    { t: "link", text: "d", url: "http://x/y" }, { t: "text", text: " e [f **g" },
  ]);
});

it("inline parsing stays linear on a huge paragraph of unmatched markers", () => {
  const text = "[a ".repeat(20_000) + "** ".repeat(20_000) + "`x ".repeat(20_000) + "[b](";
  const start = performance.now();
  const parts = parseInline(text);
  expect(performance.now() - start).toBeLessThan(500);
  expect(parts.length).toBeGreaterThan(0);
});

it("a CSV with 150k rows renders without a spread overflow", () => {
  mockHost();
  const content = Array.from({ length: 150_000 }, (_, i) => `r${i},${i}`).join("\n");
  render(<DocumentTab doc={doc({ kind: "csv", content })} onClose={() => {}} />);
  expect(screen.getAllByRole("columnheader")).toHaveLength(2);
});
