const defaults = () => ({ version: 1, displayId: null, size: 52, zoom: 1.7, magnify: true, gap: 8, bottom: 10, buttons: [
  { id: 'clock', label: '시계', icon: '◷', color: '#315966', type: 'clock', target: '24h' },
  { id: 'cpu', label: 'CPU', icon: '▥', color: '#385b47', type: 'cpu', target: '' },
  { id: 'memory', label: 'RAM', icon: '▥', color: '#604c7a', type: 'memory', target: '' },
  { id: 'web', label: '웹 열기', icon: '◎', color: '#387ee8', type: 'url', target: 'https://www.google.com' },
  { id: 'files', label: '탐색기', icon: '▰', color: '#d19a32', type: 'program', target: 'explorer.exe' },
  { id: 'notes', label: '메모장', icon: '▤', color: '#639b81', type: 'program', target: 'notepad.exe' },
  { id: 'copy', label: '복사', icon: '⧉', color: '#59687d', type: 'macro', target: 'Ctrl+C' },
  { id: 'paste', label: '붙여넣기', icon: '▣', color: '#59687d', type: 'macro', target: 'Ctrl+V' },
  { id: 'media', label: '재생 / 일시정지', icon: '▶', color: '#8d5266', type: 'media', target: 'PLAYPAUSE' }
] });
function normalize(value) {
  const d = defaults();
  if (!value || typeof value !== 'object') throw new Error('설정 형식이 올바르지 않습니다.');
  const bounded = (key, min, max) => Number.isFinite(value[key]) ? Math.min(max, Math.max(min, value[key])) : d[key];
  return { version: 1, displayId: Number.isInteger(value.displayId) ? value.displayId : null,
    size: bounded('size', 32, 80), zoom: bounded('zoom', 1, 2.2), magnify: typeof value.magnify === 'boolean' ? value.magnify : d.magnify,
    gap: bounded('gap', 4, 20), bottom: bounded('bottom', 0, 100),
    buttons: (Array.isArray(value.buttons) ? value.buttons : d.buttons).slice(0, 100).map((b, i) => {
      if (!b || !['url', 'program', 'path', 'extension', 'clock', 'cpu', 'memory', 'macro', 'media'].includes(b.type)) throw new Error('지원하지 않는 버튼 종류입니다.');
      return { id: String(b.id || i), label: String(b.label || '버튼').slice(0, 80), icon: String(b.icon || '◇').slice(0, 8),
        color: /^#[0-9a-f]{6}$/i.test(b.color) ? b.color : '#387ee8', image: /^data:image\/(png|jpeg|gif|webp);base64,[a-z0-9+/=]+$/i.test(b.image || '') && b.image.length < 4000000 ? b.image : '',
        type: b.type, target: String(b.target || '').slice(0, 4096), action: String(b.action || '').slice(0, 200),
        params: b.params && typeof b.params === 'object' && !Array.isArray(b.params) ? b.params : {} };
    }) };
}
function bounds(config, workArea) {
  const width = Math.min(workArea.width, Math.ceil((config.buttons.length + 1) * (config.size + config.gap) + config.size * config.zoom * 2 + 32));
  const height = Math.min(workArea.height, Math.ceil(config.size * config.zoom + 70));
  return { x: Math.round(workArea.x + (workArea.width - width) / 2), y: Math.round(workArea.y + workArea.height - height - config.bottom), width, height };
}
module.exports = { defaults, normalize, bounds };
