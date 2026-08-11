import { describe, expect, it } from "vitest";
import { nextScale } from "./viewer/zoom";

describe("nextScale", () => {
  it("zooms in when the wheel goes up (negative deltaY)", () => {
    expect(nextScale(1, -100)).toBeCloseTo(1.2);
  });

  it("zooms out when the wheel goes down (positive deltaY)", () => {
    expect(nextScale(1.2, 100)).toBeCloseTo(1);
  });

  it("clamps at the minimum scale", () => {
    expect(nextScale(0.1, 100)).toBe(0.1);
    expect(nextScale(0.11, 100)).toBe(0.1);
  });

  it("clamps at the maximum scale", () => {
    expect(nextScale(20, -100)).toBe(20);
    expect(nextScale(19, -100)).toBe(20);
  });

  it("ignores zero delta", () => {
    expect(nextScale(3, 0)).toBe(3);
  });
});
