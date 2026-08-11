import { describe, expect, it } from "vitest";
import { rangeSelection } from "./gallery/selection";

const IDS = ["a", "b", "c", "d", "e"];

describe("rangeSelection", () => {
  it("selects the inclusive range forward", () => {
    expect(rangeSelection(IDS, "b", "d")).toEqual(["b", "c", "d"]);
  });

  it("selects the inclusive range backward", () => {
    expect(rangeSelection(IDS, "d", "b")).toEqual(["b", "c", "d"]);
  });

  it("selects a single item when anchor equals target", () => {
    expect(rangeSelection(IDS, "c", "c")).toEqual(["c"]);
  });

  it("falls back to the target alone when the anchor is unknown", () => {
    expect(rangeSelection(IDS, "zzz", "d")).toEqual(["d"]);
  });
});
