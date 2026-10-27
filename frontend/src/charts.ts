import type { Candle } from './bridge';
export type ChartPoint = { date: string; value: number };
export type Plot = { path: string; fill: string; min: number; max: number; last: number; zeroY: number };

export function normalized(candles: Candle[]): ChartPoint[] {
  if (candles.length === 0 || candles[0].close <= 0) return [];
  const start = candles[0].close;
  return candles.map(c => ({ date: c.date, value: (c.close / start - 1) * 100 }));
}

export function buildPlot(points: ChartPoint[], width: number, height: number, domain?: [number, number]): Plot {
  if (!points.length) return { path: '', fill: '', min: 0, max: 0, last: 0, zeroY: height / 2 };
  const values = points.map(p => p.value);
  const min = domain ? domain[0] : Math.min(0, ...values);
  const max = domain ? domain[1] : Math.max(0, ...values);
  const span = max - min || 1;
  const x = (i: number) => (i / Math.max(1, points.length - 1)) * width;
  const y = (v: number) => height - ((v - min) / span) * height;
  const path = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(i).toFixed(2)},${y(p.value).toFixed(2)}`).join(' ');
  return { path, fill: `${path} L${width},${height} L0,${height} Z`, min, max, last: points.at(-1)?.value ?? 0, zeroY: y(0) };
}

export function sharedDomain(a: ChartPoint[], b: ChartPoint[]): [number, number] {
  const all = [...a, ...b].map(p => p.value);
  if (!all.length) return [-1, 1];
  const low = Math.min(0, ...all), high = Math.max(0, ...all);
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
  const path = points.map((p, i) => `${i ? 'L' : 'M'}${x(p.date).toFixed(2)},${y(p.value).toFixed(2)}`).join(' ');
  return { path, fill: '', min, max, last: points.at(-1)?.value ?? 0, zeroY: y(0) };
}
