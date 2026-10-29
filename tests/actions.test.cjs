const { test } = require('node:test');
const assert = require('node:assert/strict');
const { parseMacro, cpuPercent, inputBuffer } = require('../src/actions.cjs');
test('macro supports chords, delays and Unicode text', () => {
  assert.deepEqual(parseMacro('Ctrl+C\nwait 250\nCtrl+V\ntext 안녕하세요'), [{ keys: [17, 67] }, { wait: 250 }, { keys: [17, 86] }, { text: '안녕하세요' }]);
  assert.deepEqual(parseMacro('PLAYPAUSE'), [{ keys: [179] }]);
});
test('invalid keys and unbounded delays rejected before execution', () => {
  for (const source of ['', 'Ctrl+Ctrl+C', 'Unknown', 'wait 10001', 'wait 10000;wait 10000;wait 10000;wait 10000']) assert.throws(() => parseMacro(source));
});
test('CPU uses sample deltas rather than lifetime averages', () => {
  assert.equal(cpuPercent({ idle: 800, total: 1000 }, { idle: 850, total: 1200 }), 75);
  assert.equal(cpuPercent({ idle: 800, total: 1000 }, { idle: 800, total: 1000 }), 0);
});
test('Windows INPUT ABI encodes keyboard type, key and release flag', () => {
  const { buffer, size } = inputBuffer([{ vk: 17 }, { vk: 17, flags: 2 }]);
  const offset = process.arch === 'ia32' ? 4 : 8;
  assert.equal(buffer.length, size * 2); assert.equal(buffer.readUInt32LE(0), 1);
  assert.equal(buffer.readUInt16LE(offset), 17); assert.equal(buffer.readUInt32LE(size + offset + 4), 2);
});
