# 01 — 应用骨架 + 文件夹选择器 → Library

**What to build:** 从空仓库站起来一个 Tauri (Rust + WebView2) 桌面应用。冷启动（不带文件）显示文件夹选择器；用户选定一个文件夹后，应用扫描其当前层（不递归子目录）的全部 .svg 文件，构成一个 Library，并以文件名清单的形式展示出来。此刻还没有图形渲染，清单就是可演示的终点。

**Blocked by:** None — can start immediately

**Status:** done (commit 1e6679c)

- [ ] 应用能启动，冷启动弹出文件夹选择器
- [ ] 选定文件夹后列出该层全部 .svg 文件名，按名称排序
- [ ] 子目录中的 SVG 不出现在清单中
- [ ] 技术栈为 Tauri，遵守 ADR 0001（不用 Electron / 纯原生）
