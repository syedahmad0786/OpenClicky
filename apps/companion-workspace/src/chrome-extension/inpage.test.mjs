import { describe, expect, it } from "vitest";
import { JSDOM } from "jsdom";
import { extractMain, guardMain, fieldKind, activeField, dismissCover, pageMatches } from "../../../chrome-extension/inpage.mjs";

describe("inpage", () => {
  it("wait_for gone: true waits until the text or url is absent", () => {
    document.body.innerHTML = "<p>Loading results</p>";
    // jsdom has no innerText; the page check only needs the visible text.
    Object.defineProperty(document.body, "innerText", { configurable: true, get() { return this.textContent; } });
    try {
      expect(pageMatches({ text: "Loading" })).toBe(true);
      expect(pageMatches({ text: "Loading", gone: true })).toBe(false);
      expect(pageMatches({ text: "Done", gone: true })).toBe(true);
      expect(pageMatches({ text: "Done", gone: false })).toBe(false);
      expect(pageMatches({ url: location.href, gone: true })).toBe(false);
      expect(pageMatches({ url: "/no-such-page", gone: true })).toBe(true);
      expect(pageMatches({ text: "Done", url: location.href, gone: true })).toBe(false); // both must be gone
    } finally {
      delete document.body.innerText;
    }
  });

  it("extract skips field values and passwords", () => {
    document.body.innerHTML = `
      <h2>Account</h2>
      <form><input type="text" value="alice-typed"><input type="password" value="hunter2">
        <textarea>draft-secret</textarea><select><option selected>opt-secret</option></select></form>
      <ul><li>First <a href="https://example.test/a">link</a></li></ul>
      <p style="display:none">hidden-words</p><script>var s = "script-words";</script>
      <table><tr><th>Name</th><th>Qty</th></tr><tr><td>Pen, blue</td><td>2</td></tr><tr><td>Ink</td><td>1</td></tr></table>`;
    document.querySelector("input[type=text]").value = "alice-typed";
    document.querySelector("input[type=password]").value = "hunter2";
    const { markdown, tables } = extractMain({});
    for (const leak of ["alice-typed", "hunter2", "draft-secret", "opt-secret", "hidden-words", "script-words"]) {
      expect(markdown).not.toContain(leak);
    }
    expect(markdown).toContain("## Account");
    expect(markdown).toContain("- First [link](https://example.test/a)");
    expect(tables).toEqual(['Name,Qty\n"Pen, blue",2\nInk,1']);
  });

  it("extract respects limits", () => {
    document.body.innerHTML = Array.from({ length: 25 }, (_, i) => `<table><tr><td>t${i}</td></tr></table>`).join("")
      + `<table>${"<tr><td>r</td></tr>".repeat(250)}</table>`;
    const { tables } = extractMain({});
    expect(tables).toHaveLength(20);
    document.body.innerHTML = `<table>${"<tr><td>r</td></tr>".repeat(250)}</table><p>${"x".repeat(50000)}</p>`;
    const big = extractMain({});
    expect(big.tables[0].split("\n")).toHaveLength(200);
    expect(big.markdown.length).toBeLessThanOrEqual(40000);
  });

  it("field helpers see passwords behind labels and focus", () => {
    document.body.innerHTML = `<label for="p">Pass</label><input id="p" type="PassWord"><input id="t">`;
    expect(fieldKind.call(document.querySelector("label"))).toBe("password");
    expect(fieldKind.call(document.querySelector("#t"))).toBe("text");
    document.querySelector("#p").focus();
    expect(activeField()).toBe("password");
    document.querySelector("#t").focus();
    expect(activeField()).toBe("editable");
    document.querySelector("#t").blur();
    expect(activeField()).toBe("none");
  });

  it("dismiss stays inside the cover", () => {
    document.body.innerHTML = `<div id="app"><button id="decline" aria-label="Decline">Decline</button><button id="target">Buy</button>
      <div id="cover"><span id="hit">Offer</span></div></div>`;
    const $ = (sel) => document.querySelector(sel);
    const clicked = [];
    const arm = () => {
      for (const b of document.querySelectorAll("button")) {
        b.getClientRects = () => [{}];
        b.onclick = () => clicked.push(b.id);
      }
    };
    arm();
    document.elementFromPoint = () => $("#hit");
    expect(dismissCover.call($("#target"), 1, 1)).toBe(false);
    expect(clicked).toEqual([]);
    $("#cover").insertAdjacentHTML("beforeend", `<button id="close" aria-label="Close">×</button>`);
    arm();
    expect(dismissCover.call($("#target"), 1, 1)).toBe(true);
    expect(clicked).toEqual(["close"]);
    delete document.elementFromPoint;
  });

  it("guard blocks creating frames", () => {
    const { window } = new JSDOM(`<div id="d"></div>`, { runScripts: "outside-only", url: "https://example.test/" });
    const { document } = window;
    window.eval(`(${guardMain})()`);
    const div = document.querySelector("#d");
    const NO = "Not available to agents.";
    expect(() => document.createElement("IFRAME")).toThrow(NO);
    expect(() => document.createElementNS("http://www.w3.org/1999/xhtml", "html:frame")).toThrow(NO);
    expect(document.createElement("p").localName).toBe("p");
    expect(() => { div.innerHTML = "<p>x</p><IFRAME src=x>"; }).toThrow(NO);
    expect(() => { div.outerHTML = "<object></object>"; }).toThrow(NO);
    div.innerHTML = "<b>ok</b>";
    expect(div.innerHTML).toBe("<b>ok</b>");
    expect(() => div.insertAdjacentHTML("beforeend", "<embed>")).toThrow(NO);
    expect(() => new window.DOMParser().parseFromString("<iframe>", "text/html")).toThrow(NO);
    expect(() => document.createRange().createContextualFragment("<frame>")).toThrow(NO);
    expect(() => document.write("<p>")).toThrow(NO);
    expect(() => window.frames).toThrow(NO);
    window.close();
  });

  // Serialised into its own window, as in production, so the locked globals never leak into this file.
  it("guard blanks password values and blocks cookies", () => {
    const { window } = new JSDOM(
      `<form><input id="t" name="user" type="text"><input id="p" name="pw" type="password"></form>`,
      { runScripts: "outside-only", url: "https://example.test/" },
    );
    const { document } = window;
    const text = document.querySelector("#t");
    const pass = document.querySelector("#p");
    text.value = "visible";
    pass.value = "hunter2";
    window.eval(`(${guardMain})()`);
    expect(() => document.cookie).toThrow("Not available to agents.");
    expect(() => window.localStorage).toThrow("Not available to agents.");
    expect(pass.value).toBe("");
    expect(text.value).toBe("visible");
    expect(() => { pass.value = "x"; }).toThrow("Refused: that is a password field.");
    expect(() => { pass.type = "text"; }).toThrow("Refused: that is a password field.");
    expect(() => pass.setAttribute("type", "text")).toThrow("Refused: that is a password field.");
    expect([...new window.FormData(document.querySelector("form")).keys()]).toEqual(["user"]);
    expect(pass.type).toBe("password");
    window.close();
  });
});
