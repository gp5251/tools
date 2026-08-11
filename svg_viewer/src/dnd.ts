import { getCurrentWebviewWindow } from "@tauri-apps/api/webviewWindow";

export interface DropTarget {
  onFolder: (folder: string) => void;
  onSvg: (path: string) => void;
}

export type DropClassification =
  | { kind: "svg"; path: string }
  | { kind: "folder"; path: string }
  | null;

/// Classifies a native drop: first path only; .svg → file intent, anything
/// else → folder attempt (an unreadable one surfaces the usual scan error).
export function classifyDrop(paths: string[]): DropClassification {
  const path = paths[0];
  if (!path) return null;
  return path.toLowerCase().endsWith(".svg")
    ? { kind: "svg", path }
    : { kind: "folder", path };
}

/// Wires the whole window as a drop target with a highlight overlay while
/// dragging over it. Tauri delivers OS drag-drop as webview events.
export function attachDropTarget(target: DropTarget): void {
  const overlay = document.createElement("div");
  overlay.className = "drop-overlay";
  overlay.textContent = "松开以打开";
  document.body.append(overlay);

  try {
    void getCurrentWebviewWindow()
      .onDragDropEvent((event) => {
        const type = event.payload.type;
        if (type === "enter" || type === "over") {
          overlay.classList.add("visible");
        } else if (type === "leave") {
          overlay.classList.remove("visible");
        } else if (type === "drop") {
          overlay.classList.remove("visible");
          handleDrop(event.payload.paths, target);
        }
      })
      .catch(() => overlay.remove());
  } catch {
    // 拖拽是增强功能：不可用(测试环境/旧运行时)时不影响核心流程
    overlay.remove();
  }
}

/// Routes a classified drop to its handler. Exported for tests.
export function handleDrop(paths: string[], target: DropTarget): void {
  const drop = classifyDrop(paths);
  if (drop?.kind === "svg") target.onSvg(drop.path);
  if (drop?.kind === "folder") target.onFolder(drop.path);
}
