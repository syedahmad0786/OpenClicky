import { SearchIcon } from "./icons";

export interface SearchFieldProps {
  value: string;
  onChange: (value: string) => void;
  /** Accessible name. */
  label: string;
  placeholder?: string;
  disabled?: boolean;
}

export function SearchField({ value, onChange, label, placeholder, disabled }: SearchFieldProps) {
  return (
    <label className="oc-search">
      <SearchIcon />
      <input type="search" aria-label={label} placeholder={placeholder ?? label} value={value} disabled={disabled}
        onChange={(event) => onChange(event.target.value)} />
    </label>
  );
}
