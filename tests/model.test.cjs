const { test } = require('node:test');
const assert = require('node:assert/strict');
const { defaults, normalize, bounds } = require('../src/model.cjs');
const { ExtensionHost } = require('../src/extensions.cjs');
const path = require('node:path');
test('dock remains inside a monitor with negative coordinates', () => {
  const work = { x: -1920, y: -200, width: 1920, height: 1040 };
  const rect = bounds(defaults(), work);
  assert.ok(rect.x >= work.x); assert.ok(rect.x + rect.width <= 0);
  assert.equal(rect.y + rect.height, work.y + work.height - 10);
});
test('large docks fit narrow monitors', () => {
  const config = defaults(); config.buttons = Array(100).fill(config.buttons[0]);
  assert.equal(bounds(config, { x: 0, y: 0, width: 800, height: 600 }).width, 800);
});
test('settings constrain dimensions and strip unsafe images', () => {
  const config = defaults(); config.size = 10000; config.zoom = -2; config.buttons[0].image = 'https://example.com/tracking.png';
  const normalized = normalize(config); assert.equal(normalized.size, 80); assert.equal(normalized.zoom, 1); assert.equal(normalized.buttons[0].image, '');
  assert.throws(() => normalize({ buttons: [{ type: 'shell' }] }));
});
test('standalone extension loads, exposes actions and rejects unknown action', async () => {
  const host = new ExtensionHost(), folder = path.resolve(__dirname, '../examples/hello-extension');
  const extension = await host.load(folder); assert.equal(extension.actions[0].value, 'hello');
  await host.execute(folder, 'hello', { message: 'contract test' });
  await assert.rejects(host.execute(folder, 'missing', {}));
  await assert.rejects(host.execute('unloaded', 'hello', {}));
});
