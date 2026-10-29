let config;
const root = document.querySelector('#dock');
function render(state) {
  config = state.config; root.replaceChildren();
  root.style.gap = config.gap + 'px';
  for (const item of [...config.buttons, { id: '__settings', label: '설정', icon: '⚙', color: '#344052' }]) {
    const button = document.createElement('button'); button.title = item.label; button.setAttribute('aria-label', item.label);
    button.style.setProperty('--size', config.size + 'px'); button.style.setProperty('--color', item.color);
    button.dataset.type = item.type || ''; button.dataset.format = item.target || '';
    if (['clock', 'cpu', 'memory'].includes(item.type)) {
      button.classList.add('widget');
      const value = document.createElement('strong'); value.className = 'value'; value.textContent = '…';
      const caption = document.createElement('small'); caption.className = 'caption'; caption.textContent = item.label;
      button.append(value, caption);
    } else if (item.image) { const image = document.createElement('img'); image.src = item.image; image.alt = ''; button.append(image); }
    else { const icon = document.createElement('span'); icon.textContent = item.icon; button.append(icon); }
    const label = document.createElement('span'); label.className = 'label'; label.textContent = item.label; button.append(label);
    button.onclick = async () => {
      const result = item.id === '__settings' ? await window.dock.settings() : await window.dock.run(item.id);
      if (!result.ok) { const message = document.querySelector('#message'); message.textContent = result.error; message.style.display = 'block'; setTimeout(() => message.style.display = 'none', 5000); }
    };
    root.append(button);
  }
  // Keep a single row accessible on narrower monitors.
  root.style.overflowX = root.scrollWidth > innerWidth - 16 ? 'auto' : 'visible';
  updateWidgets();
}
let metrics = { cpu: null, memory: null };
function updateWidgets() {
  for (const button of root.querySelectorAll('.widget')) {
    const type = button.dataset.type, value = button.querySelector('.value');
    if (type === 'clock') { const now = new Date(); value.textContent = now.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit', hour12: button.dataset.format === '12h' }); button.querySelector('.caption').textContent = now.toLocaleDateString('ko-KR', { month: 'numeric', day: 'numeric', weekday: 'short' }); }
    else { value.textContent = metrics[type] === null ? '…' : metrics[type] + '%'; button.style.setProperty('--usage', (metrics[type] || 0) + '%'); }
  }
}
window.dock.metrics(value => { metrics = value; updateWidgets(); });
setInterval(updateWidgets, 1000);
window.dock.pointer(point => {
  if (!config) return;
  const rect = root.getBoundingClientRect();
  const active = config.magnify && !matchMedia('(prefers-reduced-motion: reduce)').matches && point.y >= rect.top - config.size && point.y <= rect.bottom && point.x >= rect.left && point.x <= rect.right && root.style.overflowX !== 'auto';
  const buttons = [...root.children];
  const scales = buttons.map(button => {
    const center = rect.left + button.offsetLeft + config.size / 2;
    const influence = active ? Math.max(0, 1 - Math.abs(point.x - center) / (config.size * 2.2)) : 0;
    return 1 + (config.zoom - 1) * influence * influence;
  });
  let shift = -scales.reduce((sum, scale) => sum + (scale - 1) * config.size, 0) / 2;
  buttons.forEach((button, index) => {
    const scale = scales[index], extra = (scale - 1) * config.size;
    button.style.transform = `translateX(${shift + extra / 2}px) scale(${scale})`;
    button.style.zIndex = String(Math.round((scale - 1) * 100));
    shift += extra;
  });
});
window.dock.changed(render);
window.dock.state().then(result => { if (result.ok) render(result.value); });
