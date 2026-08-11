import { describe, expect, it } from "vitest";
import { classifyDrop } from "./dnd";

describe("classifyDrop", () => {
  it("classifies an .svg path as a file drop", () => {
    expect(classifyDrop(["D:/art/hero.svg"])).toEqual({ kind: "svg", path: "D:/art/hero.svg" });
  });

  it("accepts uppercase extension and backslashes", () => {
    expect(classifyDrop(["C:\\art\\Logo.SVG"])).toEqual({ kind: "svg", path: "C:\\art\\Logo.SVG" });
  });

  it("classifies anything else as a folder attempt", () => {
    expect(classifyDrop(["D:/art/icons"])).toEqual({ kind: "folder", path: "D:/art/icons" });
  });

  it("takes only the first path of a multi-drop", () => {
    expect(classifyDrop(["D:/a", "D:/b"])).toEqual({ kind: "folder", path: "D:/a" });
  });

  it("returns null for an empty drop", () => {
    expect(classifyDrop([])).toBeNull();
  });
});
