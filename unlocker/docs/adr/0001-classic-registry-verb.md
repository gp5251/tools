# Classic registry verb for shell integration

The Force Delete context-menu entry is a classic registry verb (`*\shell` and `Directory\shell` keys invoking the engine exe), not a modern Windows 11 `IExplorerCommand` COM extension with sparse-MSIX packaging. The classic verb works on every supported Windows version, lets the engine be a plain exe in any language, and needs no packaging or signing. The known cost: on Windows 11 the entry lives one click deep under "Show more options" (or Shift+right-click). A modern-menu upgrade remains possible later without changing the engine.

## Considered Options

- **Classic registry verb** (chosen) — minimal, language-agnostic, works everywhere; buried one level deep on Win11.
- **Modern IExplorerCommand + sparse MSIX** — first-class Win11 menu placement; requires C++ COM, packaging, and signing; multiples the effort.
