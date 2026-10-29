let config, displays;
const $ = id => document.getElementById(id);
const status = text => $('status').textContent = text;
function load(state) { config = structuredClone(state.config); displays = state.displays; render(); }
function element(tag, text, className) { const el = document.createElement(tag); if (text) el.textContent = text; if (className) el.className = className; return el; }
function input(value, label, change) { const el = element('input'); el.type = 'text'; el.value = value; el.setAttribute('aria-label', label); el.placeholder = label; el.oninput = () => change(el.value); return el; }
function action(text, fn) { const el = element('button', text); el.onclick = fn; return el; }
function render() {
  $('display').replaceChildren();
  const primary = element('option', '기본 모니터'); primary.value = ''; $('display').append(primary);
  for (const d of displays) { const option = element('option', `${d.label} · ${d.width} × ${d.height}`); option.value = d.id; $('display').append(option); }
  $('display').value = config.displayId ?? '';
  for (const key of ['size', 'zoom', 'bottom']) $(key).value = config[key];
  $('magnify').checked = config.magnify;
  $('sizeValue').textContent = config.size + ' px'; $('zoomValue').textContent = config.zoom + '×';
  $('buttons').replaceChildren();
  config.buttons.forEach((b, index) => {
    const card = element('div', '', 'card'); const preview = element('div', b.image ? '' : b.icon, 'preview'); preview.style.background = b.color;
    if (b.image) { const img = element('img'); img.src = b.image; img.alt = ''; preview.append(img); }
    card.append(preview, input(b.label, '버튼 이름', v => b.label = v));
    const type = element('select'); type.setAttribute('aria-label', '동작 종류');
    for (const [value, label] of [['url', '웹사이트'], ['program', '프로그램'], ['path', '파일 / 폴더'], ['clock', '실시간 시계'], ['cpu', 'CPU 사용량'], ['memory', '메모리 사용량'], ['macro', '키 매크로 / 텍스트'], ['media', '미디어 제어'], ['extension', '확장 액션']]) { const option = element('option', label); option.value = value; type.append(option); }
    type.value = b.type; type.onchange = () => { b.type = type.value; b.target = ({ clock: '24h', macro: 'Ctrl+C', media: 'PLAYPAUSE' })[b.type] || ''; render(); }; card.append(type);
    const controls = element('div', '', 'controls');
    for (const [text, offset] of [['↑', -1], ['↓', 1]]) { const button = action(text, () => { const other = index + offset; [config.buttons[index], config.buttons[other]] = [config.buttons[other], config.buttons[index]]; render(); }); button.disabled = index + offset < 0 || index + offset >= config.buttons.length; controls.append(button); }
    controls.append(action('×', () => { config.buttons.splice(index, 1); render(); })); card.append(controls);
    if (b.type === 'macro') {
      const target = element('textarea'); target.value = b.target; target.className = 'params'; target.setAttribute('aria-label', '매크로 단계'); target.placeholder = 'Ctrl+C\nwait 300\nCtrl+V\ntext 안녕하세요'; target.oninput = () => b.target = target.value; card.append(target);
    } else if (b.type === 'media' || b.type === 'clock') {
      const target = element('select'); target.className = 'target'; target.setAttribute('aria-label', '동작 옵션');
      const choices = b.type === 'clock' ? [['24h', '24시간'], ['12h', '12시간']] : [['PLAYPAUSE', '재생 / 일시정지'], ['NEXT', '다음 곡'], ['PREVIOUS', '이전 곡'], ['STOP', '정지'], ['MUTE', '음소거'], ['VOLUMEUP', '볼륨 증가'], ['VOLUMEDOWN', '볼륨 감소']];
      for (const [value, label] of choices) { const option = element('option', label); option.value = value; target.append(option); } target.value = b.target; target.onchange = () => b.target = target.value; card.append(target);
    } else if (!['cpu', 'memory'].includes(b.type)) {
      const target = input(b.target, b.type === 'extension' ? '불러온 확장 폴더 경로' : 'URL 또는 프로그램 / 파일 경로', v => b.target = v); target.className = 'target'; card.append(target);
    }
    const details = element('div', '', 'details'); details.append(input(b.icon, '아이콘 문자', v => { b.icon = v; preview.textContent = v; }));
    const color = element('input'); color.type = 'color'; color.value = b.color; color.setAttribute('aria-label', '버튼 색상'); color.oninput = () => { b.color = color.value; preview.style.background = b.color; }; details.append(color);
    details.append(action('이미지', async () => { const result = await window.dock.image(); if (result.ok && result.value) { b.image = result.value; render(); } else if (!result.ok) status(result.error); }));
    if (b.image) details.append(action('이미지 제거', () => { b.image = ''; render(); }));
    if (b.type === 'extension') { const a = input(b.action || '', '액션 ID', v => b.action = v); a.className = 'action'; details.append(a); }
    card.append(details);
    if (b.type === 'extension') { const params = element('textarea'); params.setAttribute('aria-label', '액션 매개변수 JSON'); params.value = JSON.stringify(b.params || {}, null, 2); params.oninput = () => { try { b.params = JSON.parse(params.value); params.setCustomValidity(''); } catch { params.setCustomValidity('유효한 JSON을 입력하세요.'); } }; params.className = 'params'; card.append(params); }
    $('buttons').append(card);
  });
}
for (const key of ['size', 'zoom', 'bottom']) $(key).oninput = () => { config[key] = Number($(key).value); if ($(key + 'Value')) $(key + 'Value').textContent = config[key] + (key === 'size' ? ' px' : '×'); };
$('display').onchange = () => config.displayId = $('display').value ? Number($('display').value) : null;
$('magnify').onchange = () => config.magnify = $('magnify').checked;
$('add').onclick = () => { config.buttons.push({ id: crypto.randomUUID(), label: '새 버튼', icon: '◇', color: '#508a95', type: 'url', target: '', params: {} }); render(); };
$('save').onclick = async () => {
  for (const field of document.querySelectorAll('input,textarea')) if (!field.reportValidity()) return;
  const result = await window.dock.save(config); if (result.ok) { load(result.value); status('저장되었습니다. 독에 적용했습니다.'); } else status(result.error);
};
$('extension').onclick = async () => {
  const result = await window.dock.extension();
  if (!result.ok) { status(result.error); return; } if (!result.value) return;
  const extension = result.value; const block = element('div'); block.append(element('p', extension.name));
  for (const a of extension.actions) block.append(action('+ ' + a.label, () => { config.buttons.push({ id: crypto.randomUUID(), label: a.label, icon: '◇', color: '#806db3', type: 'extension', target: extension.path, action: a.value, params: {} }); render(); status('확장 버튼을 추가했습니다. 매개변수 입력 후 저장하세요.'); }));
  $('extensions').append(block); status('확장을 불러왔습니다.');
};
window.dock.state().then(result => { if (result.ok) load(result.value); else status(result.error); });
