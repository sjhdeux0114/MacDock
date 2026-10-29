const { contextBridge, ipcRenderer } = require('electron');
contextBridge.exposeInMainWorld('dock', {
  state: () => ipcRenderer.invoke('state'), save: config => ipcRenderer.invoke('save', config),
  run: id => ipcRenderer.invoke('run', id), settings: () => ipcRenderer.invoke('settings'),
  image: () => ipcRenderer.invoke('image'), extension: () => ipcRenderer.invoke('extension'),
  metrics: callback => ipcRenderer.on('metrics', (_event, metrics) => callback(metrics)),
  changed: callback => ipcRenderer.on('changed', (_event, state) => callback(state)),
  pointer: callback => ipcRenderer.on('pointer', (_event, point) => callback(point))
});
