import { describe, expect, it } from "vitest";
import { diffLibrary } from "./gallery/reconcile";

const entry = (name: string, mtime = 1000) => ({ name, mtime });

describe("diffLibrary", () => {
  it("detects added files", () => {
    const diff = diffLibrary([entry("a.svg")], [entry("a.svg"), entry("b.svg")]);
    expect(diff.added).toEqual([entry("b.svg")]);
    expect(diff.removed).toEqual([]);
    expect(diff.modified).toEqual([]);
  });

  it("detects removed files", () => {
    const diff = diffLibrary([entry("a.svg"), entry("b.svg")], [entry("a.svg")]);
    expect(diff.removed).toEqual(["b.svg"]);
    expect(diff.added).toEqual([]);
  });

  it("detects modified files by mtime change", () => {
    const diff = diffLibrary([entry("a.svg", 1000)], [entry("a.svg", 2000)]);
    expect(diff.modified).toEqual([entry("a.svg", 2000)]);
    expect(diff.added).toEqual([]);
    expect(diff.removed).toEqual([]);
  });

  it("reports nothing when the library is unchanged (own-op echo)", () => {
    const list = [entry("a.svg"), entry("b.svg")];
    const diff = diffLibrary(list, list);
    expect(diff.added).toEqual([]);
    expect(diff.removed).toEqual([]);
    expect(diff.modified).toEqual([]);
  });

  it("handles all three kinds at once", () => {
    const diff = diffLibrary(
      [entry("a.svg", 1), entry("b.svg", 1), entry("c.svg", 1)],
      [entry("a.svg", 2), entry("c.svg", 1), entry("d.svg", 1)],
    );
    expect(diff.modified).toEqual([entry("a.svg", 2)]);
    expect(diff.removed).toEqual(["b.svg"]);
    expect(diff.added).toEqual([entry("d.svg", 1)]);
  });
});
