// Curve math shared by the panel (app.js) and the tests (tests/js). Hardware curves are [°C, PWM 0..255]; editor
// points are [°C, %]. curveAt follows the same rule as FanCurve.PwmAt (src/IBoard.cs), and
// tests/fixtures/curves.json is checked against both.

// PWM (0..255) a header outputs at temperature t for a hardware curve.
export function curveAt(points, style, t) {
  if (!points.length) return null;
  if (t <= points[0][0]) return points[0][1];
  for (let i = 1; i < points.length; i++) {
    const [x0, y0] = points[i - 1], [x1, y1] = points[i];
    if (t < x1) return style === 'StairStepMode' ? y0 : y0 + (y1 - y0) * (t - x0) / (x1 - x0);
  }
  return points[points.length - 1][1];
}

// n points spread evenly over the same temperature range, linearly interpolated (presets are 8 points; boards vary).
export function resample(pts, n) {
  if (pts.length === n) return pts.map((p) => p.slice());
  const t0 = pts[0][0], t1 = pts[pts.length - 1][0];
  return Array.from({ length: n }, (_, k) => {
    const t = Math.round(t0 + (t1 - t0) * k / (n - 1));
    return [t, Math.round(curveAt(pts, 'SmartFanMode', t))];
  });
}

// What the backend enforces: the minimum duty, hotter never slower, 100% at the last point.
export function normalize(pts, limits) {
  for (let i = 0; i < pts.length; i++) {
    pts[i][1] = Math.max(limits.minDutyPercent, Math.min(100, pts[i][1]));
    if (i > 0) pts[i][1] = Math.max(pts[i][1], pts[i - 1][1]);
  }
  pts[pts.length - 1][1] = 100;
  return pts;
}
