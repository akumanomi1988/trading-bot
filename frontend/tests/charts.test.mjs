import test from 'node:test';
import assert from 'node:assert/strict';
import { normalized, buildPlot, sharedDomain } from '../.test-build/charts.js';

const candles = [
  {date:'2026-01-01',close:100},
  {date:'2026-01-02',close:110},
  {date:'2026-01-05',close:90},
];

test('price normalization starts at zero and uses relative returns', () => {
  assert.deepEqual(normalized(candles).map(x => Math.round(x.value)), [0,10,-10]);
});

test('empty or invalid starting price has no chart output', () => {
  assert.deepEqual(normalized([]), []);
  assert.deepEqual(normalized([{ date: '2026-01-01', close: 0 }]), []);
});

test('shared domain preserves zero, values, and headroom', () => {
  const domain = sharedDomain(normalized(candles), [{date:'2026-01-01',value:50}]);
  assert.ok(domain[0] < -10);
  assert.ok(domain[1] > 50);
});

test('constant series creates finite SVG coordinates', () => {
  const flat = [{date:'2026-01-01',value:0},{date:'2026-01-02',value:0}];
  const result = buildPlot(flat,700,170);
  assert.ok(result.path.startsWith('M'));
  assert.equal(result.path.includes('NaN'),false);
  assert.equal(result.path.includes('Infinity'),false);
  assert.ok(Number.isFinite(result.zeroY));
});

test('single bar plots safely', () => {
  const result = buildPlot([{date:'2026-01-01',value:15}],700,170);
  assert.equal(result.path.includes('NaN'),false);
  assert.equal(result.last,15);
});

test('time plot respects calendar spacing, not array index spacing', async () => {
  const {buildTimePlot} = await import('../.test-build/charts.js');
  const points = [{date:'2026-01-01',value:0},{date:'2026-01-02',value:1},{date:'2026-01-11',value:2}];
  const plot = buildTimePlot(points,100,100,[-1,3],'2026-01-01','2026-01-11');
  assert.ok(plot.path.includes('L10.00,'));
  assert.ok(plot.path.includes('L100.00,'));
});
