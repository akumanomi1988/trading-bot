import test from 'node:test';
import assert from 'node:assert/strict';

let receiver;
const queued = [];
globalThis.window = {
  chrome: { webview: {
    addEventListener(kind, handler) { assert.equal(kind,'message'); receiver = handler; },
    postMessage(request) { queued.push(request); }
  }}
};
const { bridge } = await import('../.test-build/bridge.js');

test('desktop bridge reports connection only with native host', () => {
  assert.equal(bridge.connected, true);
});

test('bridge round-trips correlated success response', async () => {
  const pending = bridge.call('assets.list');
  const request = queued.shift();
  assert.equal(request.type,'assets.list');
  assert.ok(request.id.length > 0);
  receiver({data:{id:request.id,ok:true,result:[{symbol:'TEST'}]}});
  const response = await pending;
  assert.deepEqual(response,[{symbol:'TEST'}]);
});

test('native errors are propagated to the interface', async () => {
  const pending = bridge.call('paper.order',{symbol:'TEST',quantity:99});
  const request = queued.shift();
  receiver({data:{id:request.id,ok:false,error:{code:'REQUEST_FAILED',message:'Insufficient cash'}}});
  await assert.rejects(pending,/Insufficient cash/);
});

test('unknown command syntax is rejected before posting', async () => {
  const previous = queued.length;
  await assert.rejects(bridge.call('powershell;rm'),/Invalid command/);
  assert.equal(queued.length,previous);
});

test('missing response times out', async () => {
  const pending = bridge.call('assets.series',{symbol:'TEST'},15);
  queued.shift();
  await assert.rejects(pending,/timed out/);
});
