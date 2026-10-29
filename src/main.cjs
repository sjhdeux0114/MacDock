const { app, BrowserWindow, ipcMain, screen, shell, dialog, Menu, Tray, nativeImage } = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { pathToFileURL } = require('node:url');
const { defaults, normalize, bounds } = require('./model.cjs');
const { ExtensionHost } = require('./extensions.cjs');
const { executeMacro, parseMacro, cpuSnapshot, cpuPercent } = require('./actions.cjs');
const os = require('node:os');
let dockWindow, settingsWindow, tray, timer, metricsTimer, config, configPath;
const extensions = new ExtensionHost();
const smoke = process.argv.includes('--smoke-test');
if (smoke) app.setPath('userData', path.join(__dirname, '../.local/smoke-profile'));
const html = name => path.join(__dirname, name + '.html');
const webPreferences = { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true };
const state = () => ({ config, displays: screen.getAllDisplays().map(d => ({ id: d.id, label: d.label || `모니터 ${d.id}`, width: d.size.width, height: d.size.height })) });
function position() {
  const display = screen.getAllDisplays().find(d => d.id === config.displayId) || screen.getPrimaryDisplay();
  dockWindow.setBounds(bounds(config, display.workArea));
  for (const win of [dockWindow, settingsWindow]) if (win && !win.isDestroyed()) win.webContents.send('changed', state());
}
function secure(win) {
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', event => event.preventDefault());
  win.webContents.session.setPermissionRequestHandler((_wc, _permission, callback) => callback(false));
}
function openSettings() {
  if (settingsWindow && !settingsWindow.isDestroyed()) { settingsWindow.show(); return; }
  settingsWindow = new BrowserWindow({ title: 'MacDock 설정', width: 940, height: 760, minWidth: 740, minHeight: 580, backgroundColor: '#11151d', autoHideMenuBar: true, webPreferences });
  secure(settingsWindow);
  settingsWindow.loadFile(html('settings'));
}
function handle(name, fn) {
  ipcMain.handle(name, async (event, ...args) => {
    const sender = event.senderFrame?.url;
    if (![pathToFileURL(html('dock')).href, pathToFileURL(html('settings')).href].includes(sender)) throw new Error('Untrusted caller');
    try { return { ok: true, value: await fn(...args) }; } catch (error) { return { ok: false, error: error.message }; }
  });
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on('second-instance', () => dockWindow?.showInactive());
  app.whenReady().then(() => {
    configPath = path.join(app.getPath('userData'), 'settings.json');
    config = defaults();
    if (fs.existsSync(configPath)) {
      try { config = normalize(JSON.parse(fs.readFileSync(configPath, 'utf8'))); }
      catch { dialog.showErrorBox('MacDock', '설정을 읽지 못했습니다. 기존 파일을 덮어쓰지 않도록 백업한 뒤 기본값으로 시작합니다.'); fs.copyFileSync(configPath, configPath + '.invalid-' + Date.now()); }
    }
    dockWindow = new BrowserWindow({ frame: false, transparent: true, resizable: false, hasShadow: false, skipTaskbar: true, alwaysOnTop: true, focusable: false, webPreferences });
    secure(dockWindow);
    position();
    dockWindow.loadFile(html('dock'));
    dockWindow.setIgnoreMouseEvents(true, { forward: true });
    let previousCpu = cpuSnapshot();
    metricsTimer = setInterval(() => {
      const current = cpuSnapshot();
      dockWindow.webContents.send('metrics', { cpu: cpuPercent(previousCpu, current), memory: Math.round((1 - os.freemem() / os.totalmem()) * 100) });
      previousCpu = current;
    }, 1000);
    // Only the visible dock envelope captures clicks; unused transparent space passes through.
    timer = setInterval(() => {
      if (dockWindow.isDestroyed()) return;
      const p = screen.getCursorScreenPoint(), b = dockWindow.getBounds();
      const local = { x: p.x - b.x, y: p.y - b.y };
      dockWindow.webContents.send('pointer', local);
      const baseWidth = Math.min(b.width - 16, (config.buttons.length + 1) * (config.size + config.gap) + 24);
      const near = Math.abs(local.x - b.width / 2) < baseWidth / 2 && local.y > b.height - config.size * config.zoom - 30;
      const inside = Math.abs(local.x - b.width / 2) < baseWidth / 2 + (near && config.magnify ? config.size : 0) && local.y > b.height - config.size * (near && config.magnify ? config.zoom : 1) - 30 && local.y < b.height - 4;
      dockWindow.setIgnoreMouseEvents(!inside, { forward: true });
    }, 32);
    for (const event of ['display-added', 'display-removed', 'display-metrics-changed']) screen.on(event, position);
    const icon = nativeImage.createFromDataURL('data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==');
    tray = new Tray(icon); tray.setToolTip('MacDock');
    tray.setContextMenu(Menu.buildFromTemplate([{ label: 'MacDock 설정', click: openSettings }, { label: '종료', click: () => app.quit() }]));
    tray.on('double-click', openSettings);
    handle('state', state);
    handle('settings', openSettings);
    handle('save', value => {
      const next = normalize(value);
      for (const button of next.buttons) if (button.type === 'macro') parseMacro(button.target);
      fs.mkdirSync(path.dirname(configPath), { recursive: true });
      fs.writeFileSync(configPath + '.tmp', JSON.stringify(next, null, 2));
      fs.renameSync(configPath + '.tmp', configPath);
      config = next; position(); return state();
    });
    handle('run', async id => {
      const button = config.buttons.find(b => b.id === id);
      if (!button) throw new Error('버튼을 찾을 수 없습니다.');
      if (['clock', 'cpu', 'memory'].includes(button.type)) return;
      if (button.type === 'macro' || button.type === 'media') {
        if (button.type === 'media' && !['PLAYPAUSE', 'NEXT', 'PREVIOUS', 'STOP', 'MUTE', 'VOLUMEUP', 'VOLUMEDOWN'].includes(button.target)) throw new Error('지원하지 않는 미디어 명령입니다.');
        await executeMacro(button.target);
      } else if (button.type === 'url') {
        const url = new URL(button.target);
        if (!['https:', 'http:'].includes(url.protocol)) throw new Error('HTTP/HTTPS 주소만 지원합니다.');
        await shell.openExternal(url.href);
      } else if (button.type === 'path') {
        const error = await shell.openPath(button.target); if (error) throw new Error(error);
      } else if (button.type === 'program') {
        await new Promise((resolve, reject) => { const child = spawn(button.target, [], { detached: true, stdio: 'ignore', shell: false }); child.once('error', reject); child.once('spawn', () => { child.unref(); resolve(); }); });
      } else await extensions.execute(button.target, button.action, button.params);
    });
    handle('image', async () => {
      const result = await dialog.showOpenDialog(settingsWindow, { filters: [{ name: 'Images', extensions: ['png', 'jpg', 'jpeg', 'gif', 'webp'] }], properties: ['openFile'] });
      if (result.canceled) return null;
      const file = result.filePaths[0];
      if (fs.statSync(file).size > 2900000) throw new Error('이미지는 2.9MB 이하로 선택하세요.');
      const ext = path.extname(file).slice(1).replace('jpg', 'jpeg');
      return `data:image/${ext};base64,${fs.readFileSync(file).toString('base64')}`;
    });
    handle('extension', async () => {
      const result = await dialog.showOpenDialog(settingsWindow, { title: '신뢰하는 확장 폴더 선택 (package.json 포함)', properties: ['openDirectory'] });
      if (result.canceled) return null;
      const confirmation = await dialog.showMessageBox(settingsWindow, { type: 'warning', buttons: ['취소', '확장 실행'], defaultId: 0, cancelId: 0, message: '확장은 이 컴퓨터에서 Node.js 코드를 실행합니다.', detail: '신뢰하는 개발자가 만든 패키지만 불러오세요. 현재 지원 범위는 설정 없는 execute/action 확장입니다.' });
      if (confirmation.response !== 1) return null;
      return extensions.load(result.filePaths[0]);
    });
    if (process.argv.includes('--settings')) openSettings();
    if (smoke) {
      setTimeout(async () => {
        try {
          const count = await dockWindow.webContents.executeJavaScript('document.querySelectorAll("#dock button").length');
          if (count !== config.buttons.length + 1) throw new Error('Dock did not render buttons');
          openSettings();
          settingsWindow.webContents.once('did-finish-load', () => setTimeout(async () => {
            try {
              const cards = await settingsWindow.webContents.executeJavaScript('document.querySelectorAll(".card").length');
              if (cards !== config.buttons.length) throw new Error('Settings did not render');
              fs.mkdirSync(path.join(__dirname, '../.local'), { recursive: true });
              fs.writeFileSync(path.join(__dirname, '../.local/settings-preview.png'), (await settingsWindow.webContents.capturePage()).toPNG());
              fs.writeFileSync(path.join(__dirname, '../.local/dock-preview.png'), (await dockWindow.webContents.capturePage()).toPNG());
              console.log('SMOKE OK: dock and settings rendered'); app.exit(0);
            } catch (error) { console.error(error); app.exit(1); }
          }, 700));
        } catch (error) { console.error(error); app.exit(1); }
      }, 1200);
      setTimeout(() => { console.error('Smoke timeout'); app.exit(1); }, 15000);
    }
  });
}
app.on('window-all-closed', () => {});
app.on('before-quit', () => { clearInterval(timer); clearInterval(metricsTimer); });
