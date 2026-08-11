/// The Gallery's thumbnail notch ladder: 50 / 100 / 150 / 200px.
/// Snapshots stay rasterized at 200px; sizes below are CSS-only display
/// scaling — no re-rasterization on resize.
/// 档位真源。index.html 的 data-size 按钮与 :root 默认值必须与此同步
/// （静态 HTML 无法导入，改动档位时三处一起改）。
export const THUMB_SIZES = [25, 50, 100, 150] as const;
export const DEFAULT_THUMB_SIZE = 50;

const STORAGE_KEY = "thumbnail-size";

export function nextThumbSize(current: number, direction: number): number {
  const index = THUMB_SIZES.indexOf(current as (typeof THUMB_SIZES)[number]);
  if (index === -1) return DEFAULT_THUMB_SIZE;
  const next = index + Math.sign(direction);
  return THUMB_SIZES[Math.max(0, Math.min(THUMB_SIZES.length - 1, next))];
}

/// Reads a stored size, accepting only valid notches.
export function parseThumbSize(raw: string | null): number {
  const parsed = Number(raw);
  return (THUMB_SIZES as readonly number[]).includes(parsed) ? parsed : DEFAULT_THUMB_SIZE;
}

/// Restores the persisted thumbnail size (default when absent or corrupt).
export function loadThumbSize(): number {
  try {
    return parseThumbSize(localStorage.getItem(STORAGE_KEY));
  } catch {
    return DEFAULT_THUMB_SIZE;
  }
}

/// Persists the thumbnail size; storage failure is silently ignored.
export function saveThumbSize(size: number): void {
  try {
    localStorage.setItem(STORAGE_KEY, String(size));
  } catch {
    // 持久化是增强功能：失败时下次回到默认值即可
  }
}
