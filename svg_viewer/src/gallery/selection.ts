/// Inclusive range between anchor and target within an ordered id list —
/// Shift-click semantics. Unknown anchor falls back to the target alone.
export function rangeSelection(ids: string[], anchor: string, target: string): string[] {
  const from = ids.indexOf(anchor);
  const to = ids.indexOf(target);
  if (from === -1) return [target];
  const [lo, hi] = from <= to ? [from, to] : [to, from];
  return ids.slice(lo, hi + 1);
}
