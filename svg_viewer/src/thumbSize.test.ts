import { describe, expect, it } from "vitest";
import { DEFAULT_THUMB_SIZE, parseThumbSize, nextThumbSize, THUMB_SIZES } from "./gallery/thumbSize";

describe("nextThumbSize", () => {
  it("steps up one notch", () => {
    expect(nextThumbSize(100, 1)).toBe(150);
  });

  it("steps down one notch", () => {
    expect(nextThumbSize(150, -1)).toBe(100);
  });

  it("clamps at the top", () => {
    expect(nextThumbSize(150, 1)).toBe(150);
  });

  it("clamps at the bottom", () => {
    expect(nextThumbSize(25, -1)).toBe(25);
  });

  it("falls back to the default when the current size is unknown", () => {
    expect(nextThumbSize(77, 1)).toBe(DEFAULT_THUMB_SIZE);
  });
});

describe("parseThumbSize", () => {
  it("accepts a valid stored size", () => {
    expect(parseThumbSize("100")).toBe(100);
    expect(parseThumbSize("50")).toBe(50);
  });

  it("falls back to the default for garbage", () => {
    expect(parseThumbSize(null)).toBe(DEFAULT_THUMB_SIZE);
    expect(parseThumbSize("")).toBe(DEFAULT_THUMB_SIZE);
    expect(parseThumbSize("abc")).toBe(DEFAULT_THUMB_SIZE);
    expect(parseThumbSize("77")).toBe(DEFAULT_THUMB_SIZE);
  });

  it("rejects values outside the allowed steps", () => {
    for (const size of THUMB_SIZES) {
      expect(parseThumbSize(String(size + 1))).toBe(DEFAULT_THUMB_SIZE);
    }
  });
});
