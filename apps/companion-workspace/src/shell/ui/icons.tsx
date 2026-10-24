import type { SVGProps } from "react";

/** Our own small stroke icons (16x16 viewBox); no external icon pack. */
const base = { width: 16, height: 16, viewBox: "0 0 16 16", fill: "none", stroke: "currentColor", strokeWidth: 1.8, strokeLinecap: "round", strokeLinejoin: "round", "aria-hidden": true, focusable: false } as const;

export const CheckIcon = (p: SVGProps<SVGSVGElement>) => <svg {...base} {...p}><path d="M3.5 8.5l3 3 6-7" /></svg>;
export const SearchIcon = (p: SVGProps<SVGSVGElement>) => <svg {...base} {...p}><circle cx="7" cy="7" r="4.5" /><path d="M10.5 10.5L14 14" /></svg>;
export const CloseIcon = (p: SVGProps<SVGSVGElement>) => <svg {...base} {...p}><path d="M4 4l8 8M12 4l-8 8" /></svg>;
