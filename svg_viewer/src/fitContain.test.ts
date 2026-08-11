import { describe, expect, it } from "vitest";
import { fitContain } from "./gallery/view";

describe("fitContain", () => {
  it("fits a wide image by width and centers vertically", () => {
    expect(fitContain(400, 100, 200)).toEqual({ width: 200, height: 50, dx: 0, dy: 75 });
  });

  it("fits a tall image by height and centers horizontally", () => {
    expect(fitContain(100, 400, 200)).toEqual({ width: 50, height: 200, dx: 75, dy: 0 });
  });

  it("fills the box for a square image", () => {
    expect(fitContain(300, 300, 200)).toEqual({ width: 200, height: 200, dx: 0, dy: 0 });
  });

  it("never upscales beyond the box even for tiny sources", () => {
    const fit = fitContain(2, 2, 200);
    expect(fit.width).toBe(200);
    expect(fit.height).toBe(200);
  });

  it("falls back to the whole box when the source has no dimensions", () => {
    expect(fitContain(0, 0, 200)).toEqual({ width: 200, height: 200, dx: 0, dy: 0 });
  });
});
