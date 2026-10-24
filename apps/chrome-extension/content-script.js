(() => {
  let revision = Math.trunc(performance.timeOrigin);
  let elements = new Map();
  let lastLocation = location.href;
  let lastNavigationKey = globalThis.navigation?.currentEntry?.key;
  const bump = () => {
    lastLocation = location.href;
    lastNavigationKey = globalThis.navigation?.currentEntry?.key;
    revision++;
    elements = new Map();
  };
  const refreshLocation = () => {
    const navigationKey = globalThis.navigation?.currentEntry?.key;
    if (location.href !== lastLocation || (navigationKey !== undefined && lastNavigationKey !== undefined && navigationKey !== lastNavigationKey)) bump();
  };
  addEventListener("pageshow", bump);
  addEventListener("pagehide", bump);
  addEventListener("popstate", bump);
  addEventListener("hashchange", bump);

  for (const method of ["pushState", "replaceState"]) {
    const original = history[method];
    history[method] = function (...args) {
      const result = original.apply(this, args);
      bump();
      return result;
    };
  }

  function visible(element) {
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
  }

  function writableTextControl(element) {
    if (element instanceof HTMLTextAreaElement) return true;
    if (!(element instanceof HTMLInputElement)) return false;
    return ["text", "search", "email", "url", "tel", "number"].includes(element.type);
  }

  function observedValue(element) {
    return writableTextControl(element) && element.type !== "password" ? element.value.slice(0, 2000) : undefined;
  }

  function valueFingerprint(value) {
    let hash = 2166136261;
    for (let index = 0; index < value.length; index++) hash = Math.imul(hash ^ value.charCodeAt(index), 16777619);
    return `${value.length}:${hash >>> 0}`;
  }

  function controlText(element) {
    return (element.innerText || element.textContent || element.getAttribute("aria-label") || element.getAttribute("name") || "").trim().slice(0, 500);
  }

  function targetSignature(element) {
    const signature = {
      tag: element.tagName.toLowerCase(),
      type: element instanceof HTMLInputElement ? element.type : undefined,
      role: element.getAttribute("role") || undefined,
      name: element.getAttribute("name") || undefined,
      ariaLabel: element.getAttribute("aria-label") || undefined,
      text: controlText(element),
      href: element instanceof HTMLAnchorElement ? element.href : undefined,
      disabled: "disabled" in element ? Boolean(element.disabled) : undefined,
      readOnly: element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement ? element.readOnly : undefined,
    };
    const value = observedValue(element);
    if (value !== undefined) {
      signature.value = value;
      signature.valueFingerprint = valueFingerprint(element.value);
    }
    return JSON.stringify(signature);
  }

  function setNativeValue(element, value) {
    const prototype = element instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    const setter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
    if (!setter) throw new Error("This text control does not expose a native value setter.");
    setter.call(element, value);
  }

  function observe() {
    elements = new Map();
    const links = {};
    const controls = [...document.querySelectorAll("a[href],button,input,textarea,select,[role=button]")]
      .filter(visible).slice(0, 300).map((element, index) => {
        const id = `e${index + 1}`;
        const href = element instanceof HTMLAnchorElement ? element.href : undefined;
        if (href) links[id] = href;
        elements.set(id, { element, signature: targetSignature(element) });
        const control = { id, tag: element.tagName.toLowerCase(), text: controlText(element), href };
        const value = observedValue(element);
        if (value !== undefined) control.value = value;
        return control;
      });
    return { revision, origin: location.origin, title: document.title, url: location.href, frameId: window === top ? "top" : "child", controls, links };
  }

  chrome.runtime.onMessage.addListener((message, _sender, reply) => {
    try {
      refreshLocation();
      if (message?.name === "context") return reply({ ok: true, url: location.href, origin: location.origin, title: document.title, revision });
      if (message?.name === "observe") return reply({ ok: true, observation: observe() });
      if (message?.revision !== revision) return reply({ ok: false, error: "Navigation changed; observe again." });
      const target = elements.get(message?.elementId || message?.linkId);
      const element = target?.element;
      if (!element || !element.isConnected) return reply({ ok: false, error: "Observed element is stale; observe again." });
      if (!visible(element)) return reply({ ok: false, error: "Observed element is no longer visible; observe again." });
      if (targetSignature(element) !== target.signature) return reply({ ok: false, error: "Observed element changed; observe again." });
      if (message.name === "click" || message.name === "followLink") {
        element.click();
        elements = new Map();
      }
      else if (message.name === "typeText") {
        if (!(element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)) return reply({ ok: false, error: "The observed destination is not a text control." });
        if (element instanceof HTMLInputElement && element.type === "password") return reply({ ok: false, error: "Password fields cannot be filled by the browser bridge." });
        if (!writableTextControl(element)) return reply({ ok: false, error: "The observed input type is not supported for text entry." });
        if (element.disabled) return reply({ ok: false, error: "The observed text control is disabled." });
        if (element.readOnly) return reply({ ok: false, error: "The observed text control is read-only." });
        const expectedRevision = revision;
        const value = String(message.text ?? "");
        element.focus();
        refreshLocation();
        if (revision !== expectedRevision) return reply({ ok: false, error: "Navigation changed while the text control received focus; observe again." });
        if (!element.isConnected || !visible(element)) return reply({ ok: false, error: "The text control changed while receiving focus; observe again." });
        if (targetSignature(element) !== target.signature) return reply({ ok: false, error: "The text control changed while receiving focus; observe again." });
        setNativeValue(element, value);
        element.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: value }));
        refreshLocation();
        if (revision !== expectedRevision) return reply({ ok: false, error: "Navigation changed during text entry; observe again." });
        if (!element.isConnected || !visible(element)) return reply({ ok: false, error: "The text control changed during text entry; observe again." });
        if (element.value !== value) return reply({ ok: false, error: "The page did not retain the requested text." });
        const expectedSignature = JSON.parse(target.signature);
        expectedSignature.value = value.slice(0, 2000);
        expectedSignature.valueFingerprint = valueFingerprint(value);
        if (targetSignature(element) !== JSON.stringify(expectedSignature)) return reply({ ok: false, error: "The text control changed during text entry; observe again." });
        elements = new Map();
        return reply({ ok: true, value: value.slice(0, 2000), truncated: value.length > 2000 });
      } else return reply({ ok: false, error: "Unsupported action or destination control." });
      reply({ ok: true });
    } catch (error) { reply({ ok: false, error: String(error?.message || error) }); }
    return true;
  });
})();
