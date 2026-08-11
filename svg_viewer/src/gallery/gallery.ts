import { fitContain } from "./view";
import { evictSvgText, loadSvgText, SVG_MIME } from "./cache";
import type { LibraryDiff } from "./reconcile";
import { attachPreview } from "./preview";
import { openViewer } from "../viewer/viewer";
import { clearSelection, deselectPath } from "./actions";

const SNAPSHOT_SIZE = 200;

export interface GalleryItem {
  name: string;
  path: string;
  mtime: number;
  size?: number;
  isFolder?: boolean;
}

/// Rasterizes SVG text once into a canvas — the Gallery's static snapshot.
/// Animations are frozen because the canvas holds a single drawn frame.
async function rasterize(svgText: string): Promise<HTMLCanvasElement> {
  const url = URL.createObjectURL(new Blob([svgText], { type: SVG_MIME }));
  try {
    const img = await loadImage(url);
    const canvas = document.createElement("canvas");
    canvas.width = SNAPSHOT_SIZE;
    canvas.height = SNAPSHOT_SIZE;
    const fit = fitContain(img.naturalWidth, img.naturalHeight, SNAPSHOT_SIZE);
    canvas
      .getContext("2d")!
      .drawImage(img, fit.dx, fit.dy, fit.width, fit.height);
    return canvas;
  } finally {
    URL.revokeObjectURL(url);
  }
}

function loadImage(url: string): Promise<HTMLImageElement> {
  const { promise, resolve, reject } = Promise.withResolvers<HTMLImageElement>();
  const img = new Image();
  img.onload = () => resolve(img);
  img.onerror = () => reject(new Error("unparseable SVG"));
  img.src = url;
  return promise;
}

/// Renders the Library as a single scrollable grid of snapshot cards,
/// sorted by name (caller passes sorted names). Cards rasterize lazily:
/// only when scrolled into view. Broken SVGs get a placeholder card with
/// the filename in red and never block other items.
let observer: IntersectionObserver | null = null;

export function renderGallery(container: HTMLElement, items: GalleryItem[]): void {
  observer?.disconnect();
  clearSelection();
  container.replaceChildren();
  // Preserve the current view-mode class (mode-icons/mode-list/mode-details)
  const currentMode = container.className.match(/mode-\w+/)?.[0] ?? "mode-icons";
  container.className = `gallery ${currentMode}`;

  observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      observer!.unobserve(entry.target);
      void fillCard(entry.target as HTMLElement);
    }
  });

  for (const item of items) {
    const card = makeCard(item);
    container.append(card);
    // 文件夹卡不需要懒加载光栅化，跳过观察
    if (!item.isFolder) observer!.observe(card);
  }
}

/// Builds one gallery card with all its behaviors attached.
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function makeCard(item: GalleryItem): HTMLElement {
  const card = document.createElement("div");
  if (item.isFolder) {
    card.className = "card folder-card";
    card.dataset.path = item.path;
    const frame = document.createElement("div");
    frame.className = "frame";
    frame.textContent = "📁";
    const label = document.createElement("div");
    label.className = "label";
    label.textContent = item.name;
    card.append(frame, label);
    return card;
  }
  card.className = "card pending";
  card.dataset.path = item.path;
  card.dataset.mtime = String(item.mtime);
  card.dataset.size = String(item.size ?? 0);

  const frame = document.createElement("div");
  frame.className = "frame";
  const label = document.createElement("div");
  label.className = "label";
  label.textContent = item.name;

  const detailMtime = document.createElement("span");
  detailMtime.className = "detail-mtime";
  detailMtime.textContent = item.mtime > 0 ? new Date(item.mtime).toLocaleString("zh-CN", { dateStyle: "short", timeStyle: "short" }) : "";
  const detailSize = document.createElement("span");
  detailSize.className = "detail-size";
  detailSize.textContent = item.size != null && item.size > 0 ? formatSize(item.size) : "";

  card.append(frame, label, detailMtime, detailSize);
  attachPreview(card, item.path);
  card.addEventListener("dblclick", (event) => {
    // 双击文件名是重命名，不进 Viewer
    if ((event.target as HTMLElement).closest(".label")) return;
    void openViewer({ path: item.path, name: item.name, returnTo: card });
  });
  return card;
}

/// Inserts a card keeping the Gallery's path-sorted invariant.
/// Compares by dataset.path so cards sort correctly by full path.
export function insertCardSorted(parent: HTMLElement, card: HTMLElement): void {
  const cardPath = (card.dataset.path ?? "").toLowerCase();
  for (const sibling of parent.querySelectorAll<HTMLElement>(".card")) {
    if (sibling === card) continue;
    if ((sibling.dataset.path ?? "").toLowerCase() > cardPath) {
      parent.insertBefore(card, sibling);
      return;
    }
  }
  parent.append(card);
}

/// Applies a watcher diff to the live gallery: removes gone files, inserts
/// new files at their sorted position, and re-rasterizes modified ones
/// (cache evicted so the new content is read).
export function applyLibraryDiff(
  container: HTMLElement,
  folder: string,
  diff: LibraryDiff,
): void {
  for (const name of diff.removed) {
    const path = joinPath(folder, name);
    findCardByPath(container, path)?.remove();
    deselectPath(path);
  }
  for (const entry of diff.modified) {
    const card = findCardByPath(container, joinPath(folder, entry.name));
    if (!card) continue;
    evictSvgText(card.dataset.path!);
    card.dataset.mtime = String(entry.mtime);
    card.dataset.size = String(entry.size ?? 0);
    // 更新详细信息模式的显示
    const mtimeEl = card.querySelector(".detail-mtime");
    if (mtimeEl) mtimeEl.textContent = entry.mtime > 0 ? new Date(entry.mtime).toLocaleString("zh-CN", { dateStyle: "short", timeStyle: "short" }) : "";
    const sizeEl = card.querySelector(".detail-size");
    if (sizeEl) sizeEl.textContent = entry.size != null && entry.size > 0 ? formatSize(entry.size) : "";
    // 保留 selected 等状态类，只重置渲染状态
    card.classList.remove("ready", "broken", "oversized");
    card.classList.add("pending");
    card.querySelector(".frame")!.replaceChildren();
    observer?.observe(card);
  }
  for (const entry of diff.added) {
    const card = makeCard({ name: entry.name, path: joinPath(folder, entry.name), mtime: entry.mtime, size: entry.size });
    insertCardSorted(container, card);
    observer?.observe(card);
  }
}

/// Joins a Library folder and a file name into a card path — the single
/// place that owns path shape (always forward slashes).
export function joinPath(folder: string, name: string): string {
  return `${folder}/${name}`;
}

/// The inverse of joinPath: splits any path (backslashes tolerated) into
/// folder + name.
export function splitPath(path: string): { folder: string; name: string } {
  const normalized = path.replaceAll("\\", "/");
  const split = normalized.lastIndexOf("/");
  return { folder: normalized.slice(0, split), name: normalized.slice(split + 1) };
}

/// Finds the gallery card for a file path (backslashes tolerated).
/// Card DOM knowledge stays in this module — callers ask, never query.
export function findCardByPath(container: HTMLElement, path: string): HTMLElement | null {
  const normalized = path.replaceAll("\\", "/");
  for (const card of container.querySelectorAll<HTMLElement>(".card")) {
    if (card.dataset.path === normalized) return card;
  }
  return null;
}

async function fillCard(card: HTMLElement): Promise<void> {  const path = card.dataset.path!;
  const frame = card.querySelector<HTMLElement>(".frame")!;
  try {
    const svgText = await loadSvgText(path);
    const snapshot = await rasterize(svgText);
    frame.replaceChildren(snapshot);
    card.classList.replace("pending", "ready");
  } catch (error) {
    const oversized = String(error).includes("size cap");
    frame.replaceChildren(placeholder(oversized ? "文件过大" : "无法解析"));
    card.classList.replace("pending", oversized ? "oversized" : "broken");
  }
}

function placeholder(text: string): HTMLElement {
  const el = document.createElement("div");
  el.className = "placeholder";
  el.textContent = text;
  return el;
}
