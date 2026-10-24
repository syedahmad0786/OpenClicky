import { useEffect } from "react";
import { postPage } from "../bridge";

const DEBOUNCE_MS = 16;
const MAX_RECTS = 64; // host limit (SurfaceRegions.MaxRects)
const round = (v: number) => Math.round(v * 100) / 100;

/**
 * One radius per rect: the larger bottom corner. The top edge of the notch is flush with the screen, so
 * matching the 26 px sheet corners only shaves a sliver off the 10 px top flare; the reverse (smallest corner)
 * would leave a visible hit area outside the rounded bottom.
 */
function radiusOf(el: Element, w: number, h: number): number {
  const style = getComputedStyle(el);
  const bottom = Math.max(parseFloat(style.borderBottomLeftRadius) || 0, parseFloat(style.borderBottomRightRadius) || 0);
  return round(Math.min(bottom, w / 2, h / 2));
}

/**
 * Reports every `[data-region]` element's rect and radius to the host as `surface.regions`.
 * The native window is clickable and visible only inside the union, so elements must carry their whole
 * drawn envelope in their layout box (the notch keeps its final size while animating).
 * Document order matters: the host drops rects from the end when over its area cap.
 */
export function useRegions(): void {
  useEffect(() => {
    let timer: number | undefined;
    let last = "[]"; // nothing drawn yet: do not announce an empty set
    const watched = new Set<Element>();
    const resizeObserver = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(() => schedule());

    function measure() {
      timer = undefined;
      const rects: { x: number; y: number; w: number; h: number; r: number }[] = [];
      const seen = new Set<Element>();
      for (const el of document.querySelectorAll("[data-region]")) {
        seen.add(el);
        if (!watched.has(el)) { watched.add(el); resizeObserver?.observe(el); }
        const box = el.getBoundingClientRect();
        if (!(box.width > 0 && box.height > 0) || rects.length >= MAX_RECTS) continue;
        rects.push({ x: round(box.left), y: round(box.top), w: round(box.width), h: round(box.height), r: radiusOf(el, box.width, box.height) });
      }
      for (const el of watched) if (!seen.has(el)) { watched.delete(el); resizeObserver?.unobserve(el); }
      const next = JSON.stringify(rects);
      if (next === last) return;
      last = next;
      postPage({ kind: "surface.regions", rects });
    }

    // Coalesce: one measurement 16 ms after the first trigger, reading the latest layout.
    function schedule() { timer ??= window.setTimeout(measure, DEBOUNCE_MS); }

    const mutations = new MutationObserver(schedule);
    mutations.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["class", "style", "hidden", "data-region"] });
    window.addEventListener("resize", schedule);
    document.addEventListener("transitionend", schedule, true);
    document.addEventListener("animationend", schedule, true);
    schedule();
    return () => {
      if (timer !== undefined) window.clearTimeout(timer);
      mutations.disconnect();
      resizeObserver?.disconnect();
      window.removeEventListener("resize", schedule);
      document.removeEventListener("transitionend", schedule, true);
      document.removeEventListener("animationend", schedule, true);
    };
  }, []);
}
