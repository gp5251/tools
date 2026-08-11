use parking_lot::Mutex;
use std::path::Path;
use tauri::{Emitter, Manager};

/// Scans the top layer of a folder for SVG files, sorted case-insensitively.
/// Never recurses into subdirectories.
pub fn scan_svg_files(dir: &Path) -> std::io::Result<Vec<String>> {
    let mut names: Vec<String> = walkdir::WalkDir::new(dir)
        .max_depth(1)
        .into_iter()
        .filter_map(|result| {
            let entry = match result {
                Ok(e) => e,
                Err(e) => return Some(Err(e.into_io_error().unwrap_or_else(|| {
                    std::io::Error::new(std::io::ErrorKind::Other, "walk error")
                }))),
            };
            if !entry.file_type().is_file() {
                return None;
            }
            let path = entry.path();
            if !path.extension().map_or(false, |ext| ext.to_string_lossy().to_lowercase() == "svg") {
                return None;
            }
            Some(path.strip_prefix(dir).ok().and_then(|rel| rel.to_str()).map(|s| s.replace('\\', "/")).ok_or_else(|| {
                std::io::Error::new(std::io::ErrorKind::InvalidData, "non-unicode path")
            }))
        })
        .collect::<std::io::Result<Vec<_>>>()?;
    names.sort_by_key(|name| name.to_lowercase());
    Ok(names)
}

#[tauri::command]
fn scan_library(path: String) -> Result<Vec<FileEntry>, String> {
    scan_svg_entries(Path::new(&path)).map_err(|e| e.to_string())
}

/// Lists immediate subdirectories of a folder, sorted by name.
/// Used by the Gallery to show folder cards that the user can navigate into.
#[tauri::command]
fn list_subdirectories(path: String) -> Result<Vec<String>, String> {
    let dir = Path::new(&path);
    let mut names: Vec<String> = std::fs::read_dir(dir)
        .map_err(|e| e.to_string())?
        .filter_map(|entry| entry.ok())
        .filter(|entry| entry.file_type().map(|t| t.is_dir()).unwrap_or(false))
        .filter_map(|entry| entry.file_name().into_string().ok())
        .collect();
    names.sort_by_key(|name| name.to_lowercase());
    Ok(names)
}

/// One file in a Library: name plus modification time (epoch millis, 0 when
/// unreadable). The mtime lets the watcher distinguish content changes from
/// pure renames without hashing file contents.
#[derive(Clone, Debug, PartialEq, serde::Serialize)]
pub struct FileEntry {
    pub name: String,
    pub mtime: u64,
    pub size: u64,
}

/// Like scan_svg_files but attaches modification times to each entry.
pub fn scan_svg_entries(dir: &Path) -> std::io::Result<Vec<FileEntry>> {
    let names = scan_svg_files(dir)?;
    Ok(names
        .into_iter()
        .map(|name| {
            let mtime = std::fs::metadata(dir.join(&name))
                .and_then(|meta| meta.modified())
                .ok()
                .and_then(|time| time.duration_since(std::time::UNIX_EPOCH).ok())
                .map(|duration| duration.as_millis() as u64)
                .unwrap_or(0);
            let size = std::fs::metadata(dir.join(&name))
                .map(|meta| meta.len())
                .unwrap_or(0);
            FileEntry { name, mtime, size }
        })
        .collect())
}

/// Maximum SVG file size the viewer will load into the webview.
const MAX_SVG_BYTES: u64 = 20 * 1024 * 1024;

/// Reads one SVG file's text content. Rejects non-.svg paths and files
/// beyond the size cap so the webview never ingests arbitrary data.
pub fn read_svg_file(path: &Path) -> std::io::Result<String> {
    let is_svg = path
        .extension()
        .map(|ext| ext.to_string_lossy().to_lowercase() == "svg")
        .unwrap_or(false);
    if !is_svg {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidInput,
            "not an .svg file",
        ));
    }
    let meta = std::fs::metadata(path)?;
    if meta.len() > MAX_SVG_BYTES {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            "file exceeds size cap",
        ));
    }
    std::fs::read_to_string(path)
}

#[tauri::command]
fn read_svg(path: String) -> Result<String, String> {
    read_svg_file(Path::new(&path)).map_err(|e| e.to_string())
}

/// A request to open one SVG file directly: which Library (folder) it
/// belongs to, its file name, and the full path to render.
#[derive(Clone, Debug, PartialEq, serde::Serialize)]
pub struct OpenIntent {
    pub folder: String,
    pub name: String,
    pub path: String,
}

/// Extracts an OpenIntent from process arguments: the first .svg argument
/// (Explorer passes exactly one on double-click), split into folder + name.
pub fn intent_from_args(args: &[String]) -> Option<OpenIntent> {
    let arg = args
        .iter()
        .skip(1)
        .find(|a| a.to_lowercase().ends_with(".svg"))?;
    let normalized = arg.replace('\\', "/");
    let split = normalized.rfind('/')?;
    Some(OpenIntent {
        folder: normalized[..split].to_string(),
        name: normalized[split + 1..].to_string(),
        path: arg.clone(),
    })
}

struct PendingOpen {
    intent: Mutex<Option<OpenIntent>>,
    consumed: std::sync::atomic::AtomicBool,
}

#[tauri::command]
fn take_pending_open(state: tauri::State<PendingOpen>) -> Option<OpenIntent> {
    state.consumed.store(true, std::sync::atomic::Ordering::SeqCst);
    state.intent.lock().take()
}

/// Renames an SVG within its folder; the caller edits the stem only, the
/// .svg extension is preserved. Same-stem is a no-op, existing targets are
/// rejected (the OS would error later and less clearly).
pub fn rename_svg_file(path: &Path, new_stem: &str) -> std::io::Result<std::path::PathBuf> {
    let stem = new_stem.trim();
    if stem.is_empty() || stem.contains(['/', '\\']) {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidInput,
            "invalid file name",
        ));
    }
    let target = path.with_file_name(format!("{stem}.svg"));
    if target == path {
        return Ok(target);
    }
    // Windows 文件名大小写不敏感：仅大小写不同的目标是同一个文件，允许改名
    let same_file =
        target.to_string_lossy().to_lowercase() == path.to_string_lossy().to_lowercase();
    if target.exists() && !same_file {
        return Err(std::io::Error::new(
            std::io::ErrorKind::AlreadyExists,
            "a file with that name already exists",
        ));
    }
    std::fs::rename(path, &target)?;
    Ok(target)
}

/// Deletes SVGs via the OS recycle bin — always recoverable, never permanent.
pub fn delete_svg_files(paths: &[String]) -> Result<(), String> {
    trash::delete_all(paths.iter().map(Path::new)).map_err(|e| e.to_string())
}

#[tauri::command]
fn rename_svg(path: String, new_name: String) -> Result<String, String> {
    rename_svg_file(Path::new(&path), &new_name)
        .map(|p| p.to_string_lossy().to_string())
        .map_err(|e| e.to_string())
}

#[tauri::command]
fn delete_svgs(paths: Vec<String>) -> Result<(), String> {
    delete_svg_files(&paths)
}

/// The active folder watcher. Replaced wholesale when the Library changes.
struct FolderWatcher(
    Mutex<Option<notify_debouncer_mini::Debouncer<notify::RecommendedWatcher>>>,
);

/// Watches the Library's folder (current layer only) and, after a 250ms
/// debounce, re-scans and emits `library-changed` with fresh entries. The
/// frontend reconciles the diff; our own rename/delete echo back as an
/// empty diff, so self-induced events cause no DOM churn.
/// Payload of `library-changed`: the watched folder (forward slashes) and
/// its fresh entries. The folder tag lets the frontend drop stale events
/// fired just before a folder switch.
#[derive(Clone, serde::Serialize)]
struct LibraryChanged {
    folder: String,
    entries: Vec<FileEntry>,
}

#[tauri::command]
fn watch_folder(
    app: tauri::AppHandle,
    path: String,
    recursive: bool,
    state: tauri::State<FolderWatcher>,
) -> Result<(), String> {
    let folder = std::path::PathBuf::from(&path);
    let watched = folder.clone();
    let app_handle = app.clone();
    let mut debouncer = notify_debouncer_mini::new_debouncer(
        std::time::Duration::from_millis(250),
        move |result: notify_debouncer_mini::DebounceEventResult| {
            if result.is_err() {
                return;
            }
            if let Ok(entries) = scan_svg_entries(&watched) {
                let _ = app_handle.emit(
                    "library-changed",
                    LibraryChanged {
                        folder: watched.to_string_lossy().replace('\\', "/"),
                        entries,
                    },
                );
            }
        },
    )
    .map_err(|e| e.to_string())?;
    let mode = if recursive {
        notify::RecursiveMode::Recursive
    } else {
        notify::RecursiveMode::NonRecursive
    };
    debouncer
        .watcher()
        .watch(&folder, mode)
        .map_err(|e| e.to_string())?;
    *state.0.lock() = Some(debouncer);
    Ok(())
}

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, args, _cwd| {
            // Second launch attempt: focus the existing window and, when it
            // carries an .svg argument, open that file in it. If the webview
            // hasn't consumed its startup intent yet, the JS listener may not
            // be registered — park the intent instead of emitting into the void.
            if let Some(intent) = intent_from_args(&args) {
                let pending = app.state::<PendingOpen>();
                if pending.consumed.load(std::sync::atomic::Ordering::SeqCst) {
                    let _ = app.emit("open-file", intent);
                } else {
                    *pending.intent.lock() = Some(intent);
                }
            }
            if let Some(window) = app.get_webview_window("main") {
                let _ = window.unminimize();
                let _ = window.set_focus();
            }
        }))
        .plugin(tauri_plugin_dialog::init())
        .manage(PendingOpen {
            intent: Mutex::new(intent_from_args(&std::env::args().collect::<Vec<_>>())),
            consumed: std::sync::atomic::AtomicBool::new(false),
        })
        .manage(FolderWatcher(Mutex::new(None)))
        .invoke_handler(tauri::generate_handler![scan_library, read_svg, take_pending_open, rename_svg, delete_svgs, watch_folder, list_subdirectories])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;

    fn fixture(tag: &str, files: &[&str], subdirs: &[(&str, &[&str])]) -> std::path::PathBuf {
        let root = std::env::temp_dir().join(format!("svg-viewer-test-{}-{}", std::process::id(), tag));
        let _ = fs::remove_dir_all(&root);
        fs::create_dir_all(&root).unwrap();
        for name in files {
            fs::write(root.join(name), b"<svg/>").unwrap();
        }
        for (dir, inner) in subdirs {
            let sub = root.join(dir);
            fs::create_dir_all(&sub).unwrap();
            for name in *inner {
                fs::write(sub.join(name), b"<svg/>").unwrap();
            }
        }
        root
    }

    #[test]
    fn keeps_only_svg_files() {
        let root = fixture("keeps", &["a.svg", "b.png", "c.txt", "d.SVG"], &[]);
        let names = scan_svg_files(&root).unwrap();
        assert_eq!(names, vec!["a.svg", "d.SVG"]);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn does_not_recurse_into_subdirectories() {
        let root = fixture("norecurse", &["top.svg"], &[("nested", &["inner.svg"])]);
        let names = scan_svg_files(&root).unwrap();
        assert_eq!(names, vec!["top.svg"]);
        fs::remove_dir_all(&root).unwrap();
    }
    #[test]
    fn sorts_by_name_case_insensitively() {
        let root = fixture("sort", &["zebra.svg", "Alpha.svg", "mid.svg"], &[]);
        let names = scan_svg_files(&root).unwrap();
        assert_eq!(names, vec!["Alpha.svg", "mid.svg", "zebra.svg"]);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn empty_folder_gives_empty_library() {
        let root = fixture("empty", &[], &[]);
        let names = scan_svg_files(&root).unwrap();
        assert!(names.is_empty());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn missing_folder_is_an_error() {
        let result = scan_svg_files(Path::new("D:/definitely/not/a/real/folder"));
        assert!(result.is_err());
    }

    #[test]
    fn read_svg_returns_file_content() {
        let root = fixture("readok", &["icon.svg"], &[]);
        let content = read_svg_file(&root.join("icon.svg")).unwrap();
        assert_eq!(content, "<svg/>");
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn read_svg_rejects_non_svg_extension() {
        let root = fixture("readext", &["icon.png"], &[]);
        assert!(read_svg_file(&root.join("icon.png")).is_err());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn read_svg_rejects_oversized_file() {
        let root = fixture("readbig", &[], &[]);
        let big = root.join("big.svg");
        fs::write(&big, vec![b'x'; MAX_SVG_BYTES as usize + 1]).unwrap();
        assert!(read_svg_file(&big).is_err());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn read_svg_missing_file_is_an_error() {
        let root = fixture("readmiss", &[], &[]);
        assert!(read_svg_file(&root.join("nope.svg")).is_err());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn intent_from_plain_launch_is_none() {
        assert!(intent_from_args(&["svg-viewer.exe".into()]).is_none());
    }

    #[test]
    fn intent_from_svg_arg_splits_folder_and_name() {
        let intent = intent_from_args(&["svg-viewer.exe".into(), "D:/art/icons/hero.svg".into()]).unwrap();
        assert_eq!(intent.folder, "D:/art/icons");
        assert_eq!(intent.name, "hero.svg");
        assert_eq!(intent.path, "D:/art/icons/hero.svg");
    }

    #[test]
    fn intent_accepts_windows_backslashes_and_uppercase_ext() {
        let intent = intent_from_args(&["svg-viewer.exe".into(), "C:\\art\\Logo.SVG".into()]).unwrap();
        assert_eq!(intent.name, "Logo.SVG");
        assert_eq!(intent.folder, "C:/art");
    }

    #[test]
    fn intent_ignores_non_svg_args() {
        assert!(intent_from_args(&["svg-viewer.exe".into(), "D:/art/readme.txt".into()]).is_none());
    }

    #[test]
    fn rename_moves_the_file() {
        let root = fixture("renok", &["old.svg"], &[]);
        rename_svg_file(&root.join("old.svg"), "new").unwrap();
        assert!(!root.join("old.svg").exists());
        assert!(root.join("new.svg").exists());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn rename_rejects_conflicting_target() {
        let root = fixture("renconf", &["a.svg", "b.svg"], &[]);
        assert!(rename_svg_file(&root.join("a.svg"), "b").is_err());
        assert!(root.join("a.svg").exists());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn rename_to_same_stem_is_a_noop() {
        let root = fixture("rensame", &["same.svg"], &[]);
        rename_svg_file(&root.join("same.svg"), "same").unwrap();
        assert!(root.join("same.svg").exists());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn rename_case_only_change_is_allowed() {
        let root = fixture("rencase", &["Mixed.svg"], &[]);
        let new_path = rename_svg_file(&root.join("Mixed.svg"), "mixed").unwrap();
        assert!(new_path.ends_with("mixed.svg"));
        assert!(new_path.exists());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn delete_moves_file_to_recycle_bin() {
        let root = fixture("delok", &["trash-me.svg"], &[]);
        let path = root.join("trash-me.svg");
        delete_svg_files(&[path.to_string_lossy().to_string()]).unwrap();
        assert!(!path.exists());
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn entries_carry_name_and_mtime() {
        let root = fixture("entries", &["b.svg", "a.svg"], &[]);
        let entries = scan_svg_entries(&root).unwrap();
        assert_eq!(entries.len(), 2);
        assert_eq!(entries[0].name, "a.svg"); // sorted by name
        assert_eq!(entries[1].name, "b.svg");
        assert!(entries[0].mtime > 0);
        fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn watcher_fires_on_new_file() {
        let root = fixture("watch", &["a.svg"], &[]);
        let (tx, rx) = std::sync::mpsc::channel();
        let mut debouncer = notify_debouncer_mini::new_debouncer(
            std::time::Duration::from_millis(50),
            move |result: notify_debouncer_mini::DebounceEventResult| {
                if result.is_ok() {
                    let _ = tx.send(());
                }
            },
        )
        .unwrap();
        debouncer
            .watcher()
            .watch(&root, notify::RecursiveMode::NonRecursive)
            .unwrap();
        std::thread::sleep(std::time::Duration::from_millis(100));
        fs::write(root.join("b.svg"), b"<svg/>").unwrap();
        let fired = rx.recv_timeout(std::time::Duration::from_secs(5)).is_ok();
        drop(debouncer);
        fs::remove_dir_all(&root).unwrap();
        assert!(fired);
    }
}
