import { describe, expect, it } from "vitest";
import { previewPosition } from "./gallery/preview";

const VIEWPORT = { width: 1000, height: 800 };
const SIZE = 400;

describe("previewPosition", () => {
  it("places the preview below-right of the cursor with an offset", () => {
    expect(previewPosition(100, 100, VIEWPORT, SIZE)).toEqual({ left: 116, top: 116 });
  });

  it("clamps horizontally when the cursor is near the right edge", () => {
    const pos = previewPosition(900, 100, VIEWPORT, SIZE);
    expect(pos.left).toBe(1000 - SIZE - 8);
  });

  it("clamps vertically when the cursor is near the bottom edge", () => {
    const pos = previewPosition(100, 700, VIEWPORT, SIZE);
    expect(pos.top).toBe(800 - SIZE - 8);
  });

  it("never goes negative when the cursor is at the origin", () => {
    const pos = previewPosition(0, 0, VIEWPORT, SIZE);
    expect(pos.left).toBeGreaterThanOrEqual(0);
    expect(pos.top).toBeGreaterThanOrEqual(0);
  });

  it("centers when the preview is larger than the viewport", () => {
    const pos = previewPosition(500, 400, { width: 300, height: 200 }, SIZE);
    expect(pos.left).toBe((300 - SIZE) / 2);
    expect(pos.top).toBe((200 - SIZE) / 2);
  });
});
