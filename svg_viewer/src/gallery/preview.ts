import { loadSvgText } from "./cache";

export interface Viewport {
  width: number;
  height: number;
}

export interface Position {
  left: number;
  top: number;
}

const CURSOR_OFFSET = 16;
const EDGE_MARGIN = 8;

/// Position of the Hover Preview relative to the cursor: below-right by an
/// offset, clamped into the viewport. A preview larger than the viewport is
/// centered (negative margins pull it over the cursor).
export function previewPosition(
  cursorX: number,
  cursorY: number,
  viewport: Viewport,
  size: number,
): Position {
  const clamp = (value: number, max: number) => {
    const lower = Math.min(0, (max - size) / 2);
    const upper = Math.max(lower, max - size - EDGE_MARGIN);
    return Math.max(lower, Math.min(value, upper));
  };
  return {
    left: clamp(cursorX + CURSOR_OFFSET, viewport.width),
    top: clamp(cursorY + CURSOR_OFFSET, viewport.height),
  };
}

const PREVIEW_SIZE = 400;

let previewEl: HTMLElement | null = null;
let generation = 0;

/// Attaches the Hover Preview to a gallery card: hovering shows a live,
/// enlarged rendering of the SVG (animations play) that follows the mouse.
/// The preview is a singleton — sweeping fast across cards never leaves
/// multiple previews on screen.
export function attachPreview(card: HTMLElement, path: string): void {
  card.addEventListener("mouseenter", (event) => show(path, event as MouseEvent));
  card.addEventListener("mousemove", (event) => move(event as MouseEvent));
  card.addEventListener("mouseleave", hidePreview);
}

async function show(path: string, event: MouseEvent): Promise<void> {
  const gen = ++generation;
  move(event);
  try {
    const svgText = await loadSvgText(path);
    if (gen !== generation) return; // mouse already left
    const el = ensureEl();
    const img = document.createElement("img");
    img.onload = () => {
      URL.revokeObjectURL(img.src);
      if (gen !== generation) return;
      const nameEl = document.createElement("div");
      nameEl.className = "preview-name";
      nameEl.textContent = path.split("/").pop() ?? path;
      el.replaceChildren(img, nameEl);
      el.classList.add("visible");
    };
    img.onerror = () => {
      URL.revokeObjectURL(img.src);
      if (gen === generation) hidePreview(); // unparseable: nothing to preview
    };
    img.src = URL.createObjectURL(new Blob([svgText], { type: "image/svg+xml" }));
  } catch {
    if (gen === generation) hidePreview(); // broken / oversized: nothing to preview
  }
}

function move(event: MouseEvent): void {
  const el = ensureEl();
  const pos = previewPosition(
    event.clientX,
    event.clientY,
    { width: window.innerWidth, height: window.innerHeight },
    PREVIEW_SIZE,
  );
  el.style.left = `${pos.left}px`;
  el.style.top = `${pos.top}px`;
}

function hidePreview(): void {
  generation++;
  previewEl?.classList.remove("visible");
}

/// Hides the Hover Preview immediately (used when the Viewer opens).
export { hidePreview };

function ensureEl(): HTMLElement {
  if (!previewEl) {
    previewEl = document.createElement("div");
    previewEl.className = "hover-preview";
    previewEl.style.width = `${PREVIEW_SIZE}px`;
    previewEl.style.height = `${PREVIEW_SIZE}px`;
    document.body.append(previewEl);
  }
  return previewEl;
}
