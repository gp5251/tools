import { invoke } from "@tauri-apps/api/core";

export const SVG_MIME = "image/svg+xml";

const cache = new Map<string, Promise<string>>();

/// Loads an SVG's text through the Tauri bridge, memoized per path.
/// Rejections are evicted so a later hover retries instead of caching failure.
export function loadSvgText(path: string): Promise<string> {
  let pending = cache.get(path);
  if (!pending) {
    pending = invoke<string>("read_svg", { path });
    pending.catch(() => cache.delete(path));
    cache.set(path, pending);
  }
  return pending;
}

/// Drops the memoized text for a path (file changed on disk).
export function evictSvgText(path: string): void {
  cache.delete(path);
}
