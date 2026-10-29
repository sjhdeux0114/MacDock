const fs = require('node:fs');
const path = require('node:path');
// Trusted, explicitly imported Node packages only. No Deckboard installation required.
class ExtensionHost {
  constructor() { this.loaded = new Map(); }
  async load(entry) {
    if (!path.isAbsolute(entry) || !fs.existsSync(entry)) throw new Error('확장 패키지를 찾을 수 없습니다.');
    const extension = require(entry);
    const instance = extension.default || extension;
    if (typeof instance.execute !== 'function') throw new Error('execute(action, params)를 제공하지 않는 확장입니다.');
    if (Object.keys(instance.configs || {}).length) throw new Error('설정이 필요한 확장은 아직 지원하지 않습니다.');
    const actions = (instance.selections || instance.inputs || []).filter(a => a && typeof a.value === 'string').map(a => ({ value: a.value, label: String(a.label || a.value) }));
    if (!actions.length) throw new Error('지원되는 액션 목록이 없습니다.');
    if (typeof instance.initExtension === 'function') await instance.initExtension();
    this.loaded.set(entry, { instance, actions });
    return { path: entry, name: String(instance.name || path.basename(entry)), actions };
  }
  async execute(entry, action, params) {
    const record = this.loaded.get(entry);
    if (!record) throw new Error('설정에서 확장을 먼저 불러오세요.');
    if (!record.actions.some(a => a.value === action)) throw new Error('확장 액션을 찾을 수 없습니다.');
    return record.instance.execute(action, params);
  }
}
module.exports = { ExtensionHost };
