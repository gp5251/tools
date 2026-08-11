export interface FileEntry {
  name: string;
  mtime: number;
  size?: number;
}

export interface LibraryDiff {
  added: FileEntry[];
  removed: string[];
  modified: FileEntry[];
}

/// Three-way diff between the Gallery's current entries and a fresh scan.
/// An unchanged library (e.g. the watcher echoing our own rename/delete)
/// yields three empty lists — no DOM churn.
export function diffLibrary(current: FileEntry[], incoming: FileEntry[]): LibraryDiff {
  const currentByName = new Map(current.map((e) => [e.name, e.mtime]));
  const incomingNames = new Set(incoming.map((e) => e.name));

  const added = incoming.filter((e) => !currentByName.has(e.name));
  const modified = incoming.filter(
    (e) => currentByName.has(e.name) && currentByName.get(e.name) !== e.mtime,
  );
  const removed = current.map((e) => e.name).filter((name) => !incomingNames.has(name));

  return { added, removed, modified };
}
