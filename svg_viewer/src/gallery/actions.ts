import { invoke } from "@tauri-apps/api/core";
import { rangeSelection } from "./selection";
import { insertCardSorted } from "./gallery";

const selected = new Set<string>();
let anchor: string | null = null;

/// Clears the selection — called whenever the Gallery re-renders.
export function clearSelection(): void {
  selected.clear();
  anchor = null;
}

/// Drops one path from the selection (file removed externally).
export function deselectPath(path: string): void {
  selected.delete(path);
  if (anchor === path) anchor = null;
}

/// Wires multi-select, inline rename, and recycle-bin delete onto the
/// Gallery container. Attached once; cards come and go under it.
export function attachCardActions(container: HTMLElement, statusEl: HTMLElement): void {
  container.addEventListener("click", (event) => {
    const card = (event.target as HTMLElement).closest<HTMLElement>(".card");
    if (!card) return;
    applySelection(container, card, event);
  });

  // 双击文件名 = 重命名（双击卡片其余部分 = 进 Viewer）
  container.addEventListener("dblclick", (event) => {
    const label = (event.target as HTMLElement).closest<HTMLElement>(".label");
    if (!label) return;
    event.stopPropagation();
    const card = label.closest<HTMLElement>(".card")!;
    startRename(card, statusEl);
  });

  document.addEventListener("keydown", (event) => {
    if ((event.target as HTMLElement).tagName === "INPUT") return;
    if (document.querySelector("#viewer.visible")) return;
    if (event.key === "Delete" && selected.size > 0) {
      void deleteSelected(container, statusEl);
    }
    if (event.key === "F2" && selected.size === 1) {
      const card = container.querySelector<HTMLElement>(".card.selected");
      if (card) startRename(card, statusEl);
    }
  });
}

function applySelection(container: HTMLElement, card: HTMLElement, event: MouseEvent): void {
  // 文件夹卡不参与选中——Delete/F2 只作用于 SVG 文件
  if (card.classList.contains("folder-card")) return;
  const path = card.dataset.path!;
  if (event.shiftKey && anchor) {
    const ids = [...container.querySelectorAll<HTMLElement>(".card")].map((c) => c.dataset.path!);
    selected.clear();
    for (const id of rangeSelection(ids, anchor, path)) selected.add(id);
  } else if (event.ctrlKey || event.metaKey) {
    if (selected.has(path)) selected.delete(path);
    else selected.add(path);
    anchor = path;
  } else {
    selected.clear();
    selected.add(path);
    anchor = path;
  }
  for (const el of container.querySelectorAll<HTMLElement>(".card")) {
    el.classList.toggle("selected", selected.has(el.dataset.path!));
  }
}

async function deleteSelected(container: HTMLElement, statusEl: HTMLElement): Promise<void> {
  const paths = [...selected];
  try {
    await invoke("delete_svgs", { paths });
    for (const card of container.querySelectorAll<HTMLElement>(".card.selected")) {
      card.remove();
    }
    const count = paths.length;
    clearSelection();
    statusEl.textContent = `已删除 ${count} 个文件（回收站可恢复）`;
  } catch (error) {
    statusEl.textContent = `删除失败：${error}`;
  }
}

function startRename(card: HTMLElement, statusEl: HTMLElement): void {
  const label = card.querySelector<HTMLElement>(".label")!;
  const oldName = label.textContent!;
  const stem = oldName.replace(/\.svg$/i, "");

  const input = document.createElement("input");
  input.className = "rename-input";
  input.value = stem;
  label.replaceChildren(input);
  input.focus();
  input.select();

  let done = false;
  const cancel = () => {
    if (done) return;
    done = true;
    label.textContent = oldName;
  };
  const commit = async () => {
    if (done) return;
    const newStem = input.value.trim();
    if (newStem === stem) return cancel();
    try {
      // 后端返回的是权威路径；统一成正斜杠，卡片路径只有一种形态
      const newPath = (await invoke<string>("rename_svg", {
        path: card.dataset.path,
        newName: newStem,
      })).replaceAll("\\", "/");
      done = true;
      const newName = newPath.split("/").pop()!;
      label.textContent = newName;
      if (selected.delete(card.dataset.path!)) selected.add(newPath);
      if (anchor === card.dataset.path) anchor = newPath;
      card.dataset.path = newPath;
      insertCardSorted(card.parentElement!, card);
      statusEl.textContent = `已重命名为 ${newName}`;
    } catch (error) {
      statusEl.textContent = `重命名失败：${error}`;
      input.select(); // 阻止并提示：保持编辑态
    }
  };

  input.addEventListener("keydown", (event) => {
    if (event.key === "Enter") void commit();
    if (event.key === "Escape") cancel();
  });
  input.addEventListener("blur", cancel);
}

