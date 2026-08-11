# 用 Tauri (Rust + WebView2) 作为应用壳

工具的身份是系统文件查看器，需要文件关联、双击打开等 OS 集成；同时画廊要渲染几百个 SVG，必须复用浏览器引擎而非自写渲染器。选用 Tauri：WebView2 免费提供 SVG 渲染，包体约 10MB，Windows 优先但保留跨平台可能。

考虑过：Electron（同样的 webview 渲染，包体约 150MB，对看图工具过重）；纯 WPF/WinUI 原生（SVG 渲染仍需嵌 WebView2，不如直接用 Tauri）；纯网页 + File System Access API（无法注册文件关联，与产品身份冲突）。
