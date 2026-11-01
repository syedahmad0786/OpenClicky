export function KeyCaps({ keys }: { keys: readonly string[] }) {
  return (
    <span className="oc-keycaps">
      {keys.map((key, i) => <kbd key={`${key}-${i}`} className="oc-keycap">{key}</kbd>)}
    </span>
  );
}
