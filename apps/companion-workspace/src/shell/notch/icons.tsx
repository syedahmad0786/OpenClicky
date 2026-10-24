import type { SVGProps } from "react";

// Notch-specific icons, same 16x16 stroke style as ../ui/icons.tsx (that file keeps its base private).
const base = { width: 16, height: 16, viewBox: "0 0 16 16", fill: "none", stroke: "currentColor", strokeWidth: 1.8, strokeLinecap: "round", strokeLinejoin: "round", "aria-hidden": true, focusable: false } as const;

export const WaveIcon = (p: SVGProps<SVGSVGElement>) => <svg {...base} {...p}><path d="M2 8v0M5 5v6M8 2.5v11M11 5v6M14 8v0" /></svg>;
export const ChatIcon = (p: SVGProps<SVGSVGElement>) => <svg {...base} {...p}><path d="M3 3h10a1 1 0 011 1v6a1 1 0 01-1 1H7l-3 2.5V11H3a1 1 0 01-1-1V4a1 1 0 011-1z" /></svg>;
export const MicIcon = (p: SVGProps<SVGSVGElement>) => <svg {...base} {...p}><rect x="5.5" y="1.5" width="5" height="8" rx="2.5" /><path d="M3 7.5a5 5 0 0010 0M8 12.5v2" /></svg>;
export const GearIcon = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} strokeWidth={1.5} {...p}>
    <path d="M6.9 1.5h2.2l.4 1.7 1.2.5 1.5-.9 1.6 1.6-.9 1.5.5 1.2 1.7.4v2.2l-1.7.4-.5 1.2.9 1.5-1.6 1.6-1.5-.9-1.2.5-.4 1.7H6.9l-.4-1.7-1.2-.5-1.5.9-1.6-1.6.9-1.5-.5-1.2-1.7-.4V6.9l1.7-.4.5-1.2-.9-1.5 1.6-1.6 1.5.9 1.2-.5z" />
    <circle cx="8" cy="8" r="2.2" />
  </svg>
);
