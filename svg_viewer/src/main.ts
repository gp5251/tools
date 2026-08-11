import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { open } from "@tauri-apps/plugin-dialog";
import { getCurrentWindow, LogicalSize } from "@tauri-apps/api/window";
import { renderGallery, findCardByPath, applyLibraryDiff, joinPath, splitPath } from "./gallery/gallery";
import { attachCardActions } from "./gallery/actions";
import { diffLibrary } from "./gallery/reconcile";
import { attachDropTarget } from "./dnd";
import { loadThumbSize, nextThumbSize, saveThumbSize } from "./gallery/thumbSize";
import type { FileEntry } from "./gallery/reconcile";
import { openViewer } from "./viewer/viewer";

const pickButton = document.querySelector<HTMLButtonElement>("#pick")!;
const goUpButton = document.querySelector<HTMLButtonElement>("#go-up")!;
const statusEl = document.querySelector<HTMLParagraphElement>("#status")!;
const galleryEl = document.querySelector<HTMLElement>("#gallery")!;

interface OpenIntent {
  folder: string;
  name: string;
  path: string;
}

let currentFolder: string | null = null;

async function showLibrary(folder: string): Promise<void> {
  statusEl.textContent = "扫描中…";
  const [entries, subdirs] = await Promise.all([
    invoke<FileEntry[]>("scan_library", { path: folder }),
    invoke<string[]>("list_subdirectories", { path: folder }),
  ]);
  const svgCount = entries.length;
  const dirCount = subdirs.length;
  const parts: string[] = [];
  if (svgCount > 0) parts.push(`${svgCount} 个 SVG`);
  if (dirCount > 0) parts.push(`${dirCount} 个子文件夹`);
  statusEl.textContent = `${folder} — ${parts.join(", ") || "空文件夹"}`;
  renderGallery(
    galleryEl,
    [
      ...subdirs.map((name) => ({
        name,
        path: joinPath(folder, name),
        mtime: 0,
        size: 0,
        isFolder: true,
      })),
      ...entries.map((entry) => ({
        name: entry.name,
        path: joinPath(folder, entry.name),
        mtime: entry.mtime,
        size: entry.size,
        isFolder: false,
      })),
    ],
  );
  currentFolder = folder;
  // 盘符根目录（如 C:/）没有上一级，禁用按钮
  const { folder: parent } = splitPath(folder);
  goUpButton.disabled = !parent || parent === folder;
  document.body.classList.remove("no-folder");
  // 打开文件夹后放大窗口
  try {
    await getCurrentWindow().setSize(new LogicalSize(1024, 768));
  } catch { /* 浏览器环境忽略 */ }
  void invoke("watch_folder", { path: folder, recursive: false }).catch(() => {
    // 监听失败不阻塞浏览，只是失去自动刷新
  });
}

/// 画廊当前条目的真实来源是 DOM（重命名/删除已同步过）。
/// 只统计 SVG 卡片，文件夹卡不参与 reconcile。
function currentEntries(): FileEntry[] {
  return [...galleryEl.querySelectorAll<HTMLElement>(".card:not(.folder-card)")].map((card) => ({
    name: card.dataset.path!.split("/").pop()!,
    mtime: Number(card.dataset.mtime ?? 0),
  }));
}

async function openFolder(folder: string): Promise<void> {
  try {
    await showLibrary(folder);
  } catch (error) {
    statusEl.textContent = `扫描失败：${error}`;
  }
}

async function pickFolder() {
  const folder = await open({ directory: true, title: "选择 SVG 文件夹" });
  if (!folder) return;
  await openFolder(folder);
}

/// Opens the app on one file (Explorer double-click): its folder becomes
/// the Library, and the file itself goes straight into the Viewer.
async function openFileIntent(intent: OpenIntent): Promise<void> {
  try {
    await showLibrary(intent.folder);
  } catch (error) {
    statusEl.textContent = `扫描失败：${error}`;
    return;
  }
  const card = findCardByPath(galleryEl, intent.path);
  if (card) {
    void openViewer({ path: intent.path, name: intent.name, returnTo: card });
  }
}

pickButton.addEventListener("click", pickFolder);
goUpButton.addEventListener("click", () => {
  if (!currentFolder) return;
  const { folder: parent } = splitPath(currentFolder);
  if (parent && parent !== currentFolder) void openFolder(parent);
});
attachCardActions(galleryEl, statusEl);
// 文件夹卡双击进入子目录
galleryEl.addEventListener("dblclick", (event) => {
  const card = (event.target as HTMLElement).closest(".folder-card") as HTMLElement | null;
  if (!card) return;
  event.stopPropagation();
  void openFolder(card.dataset.path!);
}, true); // capture 阶段拦截，先于 SVG 卡的 dblclick
// 缩略图档位：工具栏按钮 + Ctrl 滚轮，localStorage 持久化
let thumbSize = loadThumbSize();
const sizeButtons = document.querySelectorAll<HTMLElement>("#thumb-sizes button");

function renderThumbSize(size: number): void {
  thumbSize = size;
  document.documentElement.style.setProperty("--thumb-size", `${size}px`);
  for (const button of sizeButtons) {
    button.classList.toggle("active", Number(button.dataset.size) === size);
  }
}

function applyThumbSize(size: number): void {
  renderThumbSize(size);
  saveThumbSize(size);
}

for (const button of sizeButtons) {
  button.addEventListener("click", () => applyThumbSize(Number(button.dataset.size)));
}

galleryEl.addEventListener(
  "wheel",
  (event) => {
    if (!event.ctrlKey) return;
    event.preventDefault();
    applyThumbSize(nextThumbSize(thumbSize, event.deltaY < 0 ? 1 : -1));
  },
  { passive: false },
);

// 启动时恢复持久化档位——只渲染 UI，不重写存储
renderThumbSize(thumbSize);

// 视图模式：图标 / 列表 / 详细信息
type ViewMode = "icons" | "list" | "details";
const VIEW_MODE_KEY = "view-mode";
const viewButtons = document.querySelectorAll<HTMLElement>("#view-modes button");

function loadViewMode(): ViewMode {
  try {
    const stored = localStorage.getItem(VIEW_MODE_KEY);
    if (stored === "icons" || stored === "list" || stored === "details") return stored;
  } catch { /* ignore */ }
  return "icons";
}

function saveViewMode(mode: ViewMode): void {
  try { localStorage.setItem(VIEW_MODE_KEY, mode); } catch { /* ignore */ }
}

function renderViewMode(mode: ViewMode): void {
  galleryEl.className = `gallery mode-${mode}`;
  document.body.classList.toggle("hide-thumb-sizes", mode !== "icons");
  for (const button of viewButtons) {
    button.classList.toggle("active", button.dataset.mode === mode);
  }
}

for (const button of viewButtons) {
  button.addEventListener("click", () => {
    const mode = button.dataset.mode as ViewMode;
    renderViewMode(mode);
    saveViewMode(mode);
  });
}

renderViewMode(loadViewMode());

// 全窗口拖放：文件夹 → Library；单个 .svg → 与双击同语义
attachDropTarget({
  onFolder: (folder) => void openFolder(folder),
  onSvg: (path) => {
    void openFileIntent({ ...splitPath(path), path });
  },
});

interface LibraryChanged {
  folder: string;
  entries: FileEntry[];
}

// 文件夹监听：外部增删改 → reconcile diff（自身操作的回声 diff 为空，不抖动）。
// 换文件夹瞬间的旧事件直接丢弃，防止污染新画廊。
listen<LibraryChanged>("library-changed", (event) => {
  if (!currentFolder) return;
  if (event.payload.folder.toLowerCase() !== currentFolder.toLowerCase()) return;
  applyLibraryDiff(galleryEl, currentFolder, diffLibrary(currentEntries(), event.payload.entries));
});

// 已开实例上双击新文件 → 聚焦并换图。await 保证注册完成后再取 pending，
// 与 Rust 侧 consumed 标志共同封死 listen 竞态。
await listen<OpenIntent>("open-file", (event) => void openFileIntent(event.payload));

// 冷启动：带文件参数 → 直进 Viewer；否则弹文件夹选择器
const pending = await invoke<OpenIntent | null>("take_pending_open");
if (pending) {
  await openFileIntent(pending);
} else {
  void pickFolder();
}
