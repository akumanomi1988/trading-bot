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

test('extrema sampler preserves endpoints, original order and large drawdown spikes', async () => {
  const {extremaSampleIndices, MAX_RENDER_POINTS} = await import('../.test-build/charts.js');
  const points = Array.from({length:100000}, (_,i) => ({date: new Date(Date.UTC(2000,0,1+i)).toISOString().slice(0,10), value: i/1000}));
  points[50000].value=-40;
  points[50001].value=90;
  const indices = extremaSampleIndices(points);
  assert.ok(indices.length <= MAX_RENDER_POINTS);
  assert.equal(indices[0],0);
  assert.equal(indices.at(-1),99999);
  assert.ok(indices.includes(50000) && indices.includes(50001));
  assert.deepEqual(indices,[...indices].sort((a,b)=>a-b));
  assert.equal(new Set(indices).size,indices.length);
});
test('100k-point SVG charts are bounded while preserving time coordinates and extrema', async () => {
  const {buildTimePlot,MAX_RENDER_POINTS} = await import('../.test-build/charts.js');
  const points = Array.from({length:100000}, (_,i) => ({date:new Date(Date.UTC(2000,0,1+i)).toISOString().slice(0,10),value:0}));
  points[50000].value=100;
  points[50001].value=-100;
  const indexed = buildPlot(points,1000,100);
  const timed = buildTimePlot(points,1000,100,[-110,110],points[0].date,points.at(-1).date);
  for(const plot of [indexed,timed]) {
    assert.ok(plot.path.length < MAX_RENDER_POINTS * 21);
    assert.ok((plot.path.match(/[ML]/g)||[]).length <= MAX_RENDER_POINTS);
    assert.ok(plot.path.includes('0.00,'));
    assert.ok(!plot.path.includes('NaN') && !plot.path.includes('Infinity'));
  }
  assert.equal(indexed.min,-100);
  assert.equal(indexed.max,100);
  assert.equal(indexed.last,0);
  assert.deepEqual(sharedDomain(points,[]).map(Math.sign),[-1,1]);
});
test('small charts remain identical and sampling limit is guarded', async () => {
  const {extremaSampleIndices} = await import('../.test-build/charts.js');
  assert.deepEqual(extremaSampleIndices([{date:'2026-01-01',value:2},{date:'2026-01-02',value:3}]),[0,1]);
  assert.throws(()=>extremaSampleIndices([{date:'2026-01-01',value:0}],Number.POSITIVE_INFINITY),RangeError);
});
