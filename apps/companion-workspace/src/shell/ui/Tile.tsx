import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cx } from "./cx";
import { CheckIcon } from "./icons";

export interface TileProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "children"> {
  icon?: ReactNode;
  name: string;
  descriptor?: string;
  selected: boolean;
}

export function Tile({ icon, name, descriptor, selected, className, type = "button", ...rest }: TileProps) {
  return (
    <button type={type} aria-pressed={selected} className={cx("oc-tile", selected && "is-selected", className)} {...rest}>
      {selected ? <span className="oc-tile__check"><CheckIcon width={10} height={10} strokeWidth={2.4} /></span> : null}
      {icon ? <span className="oc-tile__icon">{icon}</span> : null}
      <span className="oc-tile__name">{name}</span>
      {descriptor ? <span className="oc-tile__desc">{descriptor}</span> : null}
    </button>
  );
}
