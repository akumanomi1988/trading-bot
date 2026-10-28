import type { Candle } from './bridge';
export type ChartPoint = { date: string; value: number };
export type Plot = { path: string; fill: string; min: number; max: number; last: number; zeroY: number };

export const MAX_RENDER_POINTS = 1200;

// Preserve the first and last observation and every bucket's extrema in chronological
// order. Indexes are returned to preserve original X spacing in index-based plots.
// Cost is O(n), while SVG output remains bounded independently of input history size.
export function extremaSampleIndices(points: readonly ChartPoint[], maxPoints = MAX_RENDER_POINTS): number[] {
  const n = points.length;
  const cap = Math.max(4, Math.floor(maxPoints));
  if (!Number.isFinite(cap)) throw new RangeError('A finite render-point limit is required.');
  if (n <= cap) return Array.from({ length: n }, (_, i) => i);
  const bucketCount = Math.floor((cap - 2) / 2);
  const lows = new Array<number>(bucketCount).fill(-1);
  const highs = new Array<number>(bucketCount).fill(-1);
  for (let i = 1; i < n - 1; i++) {
    const bucket = Math.floor((i - 1) * bucketCount / (n - 2));
    if (lows[bucket] < 0 || points[i].value < points[lows[bucket]].value) lows[bucket] = i;
    if (highs[bucket] < 0 || points[i].value > points[highs[bucket]].value) highs[bucket] = i;
  }
  const indices: number[] = [0];
  for (let bucket = 0; bucket < bucketCount; bucket++) {
    const low = lows[bucket], high = highs[bucket];
    if (low < 0) continue;
    if (low <= high) {
      indices.push(low);
      if (high !== low) indices.push(high);
    } else {
      indices.push(high, low);
    }
  }
  indices.push(n - 1);
  return indices;
}

export function normalized(candles: Candle[]): ChartPoint[] {
  if (candles.length === 0 || candles[0].close <= 0) return [];
  const start = candles[0].close;
  return candles.map(c => ({ date: c.date, value: (c.close / start - 1) * 100 }));
}

export function buildPlot(points: ChartPoint[], width: number, height: number, domain?: [number, number]): Plot {
  if (!points.length) return { path: '', fill: '', min: 0, max: 0, last: 0, zeroY: height / 2 };
  let rawMin = 0, rawMax = 0;
  if (!domain) for (const p of points) {
    rawMin = Math.min(rawMin, p.value);
    rawMax = Math.max(rawMax, p.value);
  }
  const min = domain ? domain[0] : rawMin;
  const max = domain ? domain[1] : rawMax;
  const span = max - min || 1;
  const x = (i: number) => (i / Math.max(1, points.length - 1)) * width;
  const y = (v: number) => height - ((v - min) / span) * height;
  const path = extremaSampleIndices(points).map((index, i) =>
    `${i === 0 ? 'M' : 'L'}${x(index).toFixed(2)},${y(points[index].value).toFixed(2)}`).join(' ');
  return { path, fill: `${path} L${width},${height} L0,${height} Z`, min, max, last: points.at(-1)?.value ?? 0, zeroY: y(0) };
}

export function sharedDomain(a: ChartPoint[], b: ChartPoint[]): [number, number] {
  if (!a.length && !b.length) return [-1, 1];
  let low = 0, high = 0;
  for (const point of a) { low = Math.min(low, point.value); high = Math.max(high, point.value); }
  for (const point of b) { low = Math.min(low, point.value); high = Math.max(high, point.value); }
  const pad = (high - low || 2) * 0.08;
  return [low - pad, high + pad];
}

// Overlay only: map observations to a shared UTC calendar axis rather than array positions.
export function buildTimePlot(points: ChartPoint[], width: number, height: number,
  domain: [number, number], firstDate: string, lastDate: string): Plot {
  if (!points.length) return buildPlot(points, width, height, domain);
  const time = (date: string) => Date.parse(`${date}T00:00:00Z`);
  const begin = time(firstDate), end = time(lastDate);
  const spanTime = Math.max(1, end - begin);
  const [min, max] = domain;
  const spanValue = max - min || 1;
  const x = (date: string) => (time(date) - begin) / spanTime * width;
  const y = (value: number) => height - ((value - min) / spanValue) * height;
  const path = extremaSampleIndices(points).map((index, i) => {
    const p = points[index];
    return `${i ? 'L' : 'M'}${x(p.date).toFixed(2)},${y(p.value).toFixed(2)}`;
  }).join(' ');
  return { path, fill: '', min, max, last: points.at(-1)?.value ?? 0, zeroY: y(0) };
}
