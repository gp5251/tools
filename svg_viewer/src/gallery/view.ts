export interface FitBox {
  width: number;
  height: number;
  dx: number;
  dy: number;
}

/// Computes a contain-fit rectangle for a source of `naturalWidth` x
/// `naturalHeight` inside a square box of side `box`, centered.
/// A dimensionless source (0x0) fills the whole box.
export function fitContain(naturalWidth: number, naturalHeight: number, box: number): FitBox {
  if (naturalWidth <= 0 || naturalHeight <= 0) {
    return { width: box, height: box, dx: 0, dy: 0 };
  }
  const scale = Math.min(box / naturalWidth, box / naturalHeight);
  const width = naturalWidth * scale;
  const height = naturalHeight * scale;
  return { width, height, dx: (box - width) / 2, dy: (box - height) / 2 };
}
