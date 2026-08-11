import { loadSvgText, SVG_MIME } from "../gallery/cache";
import { hidePreview } from "../gallery/preview";
import { nextScale } from "./zoom";

export interface ViewerTarget {
  path: string;
  name: string;
  returnTo: HTMLElement;
}

let viewerEl: HTMLElement | null = null;
let stageEl: HTMLElement | null = null;
let imgEl: HTMLImageElement | null = null;
let nameEl: HTMLElement | null = null;
let returnCard: HTMLElement | null = null;
let scale = 1;

/// Opens the Viewer on one SVG: full-screen checkerboard stage with the
/// live image (animations play), wheel zoom, one-key return to the Gallery
/// that scrolls the originating card back into view.
export async function openViewer(target: ViewerTarget): Promise<void> {
  hidePreview();
  returnCard = target.returnTo;
  scale = 1;
  ensureEl();
  nameEl!.textContent = target.name;
  viewerEl!.classList.add("visible");
  imgEl!.style.transform = "scale(1)";

  try {
    const svgText = await loadSvgText(target.path);
    if (returnCard !== target.returnTo) return; // already closed
    revokeCurrentUrl();
    imgEl!.onload = revokeCurrentUrl;
    imgEl!.src = URL.createObjectURL(new Blob([svgText], { type: SVG_MIME }));
  } catch {
    closeViewer(); // broken / oversized: nothing to show
  }
}

function revokeCurrentUrl(): void {
  if (imgEl?.src.startsWith("blob:")) URL.revokeObjectURL(imgEl.src);
}

export function closeViewer(): void {
  viewerEl?.classList.remove("visible");
  revokeCurrentUrl();
  returnCard?.scrollIntoView({ block: "center" });
  returnCard = null;
}

function ensureEl(): void {
  if (viewerEl) return;

  viewerEl = document.createElement("div");
  viewerEl.id = "viewer";

  const header = document.createElement("header");
  const back = document.createElement("button");
  back.id = "viewer-back";
  back.textContent = "← 返回画廊";
  back.addEventListener("click", closeViewer);
  nameEl = document.createElement("span");
  nameEl.id = "viewer-name";
  header.append(back, nameEl);

  stageEl = document.createElement("div");
  stageEl.className = "stage";
  imgEl = document.createElement("img");
  imgEl.alt = "";
  stageEl.append(imgEl);

  stageEl.addEventListener(
    "wheel",
    (event) => {
      event.preventDefault();
      scale = nextScale(scale, event.deltaY);
      imgEl!.style.transform = `scale(${scale})`;
    },
    { passive: false },
  );

  viewerEl.append(header, stageEl);
  document.body.append(viewerEl);

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && viewerEl!.classList.contains("visible")) {
      closeViewer();
    }
  });
}
