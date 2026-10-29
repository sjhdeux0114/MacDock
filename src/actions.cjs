const os = require('node:os');
const KEYS = { CTRL: 0x11, CONTROL: 0x11, ALT: 0x12, SHIFT: 0x10, WIN: 0x5b, ENTER: 0x0d, TAB: 0x09, ESC: 0x1b, ESCAPE: 0x1b, SPACE: 0x20, BACKSPACE: 0x08, DELETE: 0x2e, INSERT: 0x2d, HOME: 0x24, END: 0x23, PAGEUP: 0x21, PAGEDOWN: 0x22, LEFT: 0x25, UP: 0x26, RIGHT: 0x27, DOWN: 0x28, PRINTSCREEN: 0x2c,
  MUTE: 0xad, VOLUMEDOWN: 0xae, VOLUMEUP: 0xaf, NEXT: 0xb0, PREVIOUS: 0xb1, STOP: 0xb2, PLAYPAUSE: 0xb3 };
for (let i = 1; i <= 24; i++) KEYS['F' + i] = 0x6f + i;
const extended = new Set([0x5b, 0x2e, 0x2d, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28, 0x2c, 0xad, 0xae, 0xaf, 0xb0, 0xb1, 0xb2, 0xb3]);
function parseMacro(source) {
  const steps = source.split(/[\n;]/).map(s => s.trim()).filter(Boolean);
  if (!steps.length || steps.length > 100) throw new Error('매크로는 1~100단계로 입력하세요.');
  let duration = 0;
  return steps.map(step => {
    const delay = /^wait\s+(\d+)$/i.exec(step);
    if (delay) { const ms = Number(delay[1]); duration += ms; if (ms > 10000 || duration > 30000) throw new Error('대기는 한 단계 10초, 전체 30초 이하입니다.'); return { wait: ms }; }
    if (/^text /i.test(step)) { const text = step.slice(5); if (text.length > 2000) throw new Error('텍스트는 2000자 이하입니다.'); return { text }; }
    const keys = step.split('+').map(s => s.trim().toUpperCase());
    if (!keys.length || keys.length > 5 || new Set(keys).size !== keys.length) throw new Error('키 조합 형식이 올바르지 않습니다.');
    return { keys: keys.map(key => { const vk = KEYS[key] || (/^[A-Z0-9]$/.test(key) ? key.charCodeAt(0) : 0); if (!vk) throw new Error('지원하지 않는 키: ' + key); return vk; }) };
  });
}
function inputBuffer(events) {
  const size = process.arch === 'ia32' ? 28 : 40, offset = process.arch === 'ia32' ? 4 : 8;
  const buffer = Buffer.alloc(events.length * size);
  events.forEach((event, i) => { const start = i * size; buffer.writeUInt32LE(1, start); buffer.writeUInt16LE(event.vk || 0, start + offset); buffer.writeUInt16LE(event.scan || 0, start + offset + 2); buffer.writeUInt32LE(event.flags || 0, start + offset + 4); });
  return { buffer, size };
}
let sendInput;
function send(events) {
  if (process.platform !== 'win32') throw new Error('키 입력은 Windows에서만 지원합니다.');
  if (!sendInput) { const koffi = require('koffi'); sendInput = koffi.load('user32.dll').func('uint32_t __stdcall SendInput(uint32_t count, const void *inputs, int size)'); }
  const { buffer, size } = inputBuffer(events);
  const sent = sendInput(events.length, buffer, size);
  if (sent !== events.length) {
    // Best-effort release to avoid leaving synthetic modifier keys pressed.
    const releases = events.filter(e => !(e.flags & 2)).reverse().map(e => ({ ...e, flags: (e.flags || 0) | 2 }));
    const cleanup = inputBuffer(releases); if (releases.length) sendInput(releases.length, cleanup.buffer, cleanup.size);
    throw new Error('키 입력이 차단되었습니다. 대상 앱이 관리자 권한으로 실행 중인지 확인하세요.');
  }
}
let busy = false;
async function executeMacro(source) {
  const steps = parseMacro(source);
  if (busy) throw new Error('다른 매크로가 실행 중입니다.');
  busy = true;
  try {
    for (const step of steps) {
      if ('wait' in step) await new Promise(resolve => setTimeout(resolve, step.wait));
      else if ('text' in step) {
        const events = []; for (let i = 0; i < step.text.length; i++) { events.push({ scan: step.text.charCodeAt(i), flags: 4 }, { scan: step.text.charCodeAt(i), flags: 6 }); } send(events);
      } else {
        const downs = step.keys.map(vk => ({ vk, flags: extended.has(vk) ? 1 : 0 }));
        send([...downs, ...downs.toReversed().map(e => ({ ...e, flags: e.flags | 2 }))]);
      }
    }
  } finally { busy = false; }
}
function cpuSnapshot() { return os.cpus().reduce((acc, cpu) => { acc.idle += cpu.times.idle; acc.total += Object.values(cpu.times).reduce((a, b) => a + b, 0); return acc; }, { idle: 0, total: 0 }); }
function cpuPercent(previous, current) { const total = current.total - previous.total; return total > 0 ? Math.max(0, Math.min(100, Math.round((1 - (current.idle - previous.idle) / total) * 100))) : 0; }
module.exports = { parseMacro, executeMacro, cpuSnapshot, cpuPercent, inputBuffer };
