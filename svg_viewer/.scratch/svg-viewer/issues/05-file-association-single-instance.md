# 05 — 文件关联 + 单实例

**What to build:** 应用注册 .svg 文件关联。用户在 Explorer 双击一个 .svg 文件时，应用启动并直接进入该图的 Viewer（而非 Gallery）。应用保持单实例：已打开时再双击另一个 .svg，聚焦现有窗口并切换到新图的 Viewer，不开第二个窗口。从文件启动时，该文件所在文件夹的当前层即成为 Library，可正常切到 Gallery。

**Blocked by:** 04 — Viewer 视图

**Status:** done

- [ ] 双击 .svg 文件打开应用并进入该图的 Viewer
- [ ] 应用已开时再次双击其他 .svg：聚焦现有窗口并换图，不产生第二实例
- [ ] 从文件启动后可一键切到该文件所在文件夹的 Gallery
