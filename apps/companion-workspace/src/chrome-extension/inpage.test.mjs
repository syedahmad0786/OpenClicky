import { describe, expect, it } from "vitest";
import { JSDOM } from "jsdom";
import { extractMain, guardMain, fieldKind, activeField } from "../../../chrome-extension/inpage.mjs";

describe("inpage", () => {
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
