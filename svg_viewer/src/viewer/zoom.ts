const STEP = 1.2;
const MIN_SCALE = 0.1;
const MAX_SCALE = 20;

/// Next Viewer zoom level after a wheel event: up (negative deltaY) zooms
/// in, down zooms out, clamped to [0.1, 20].
export function nextScale(current: number, deltaY: number): number {
  if (deltaY === 0) return current;
  const next = deltaY < 0 ? current * STEP : current / STEP;
  return Math.min(MAX_SCALE, Math.max(MIN_SCALE, next));
}
