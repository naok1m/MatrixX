const state = {
  isRunning: false,
  isConnecting: false,
  connectionStatus: 'Disconnected',
  activeProfileName: 'Default',
  gameProfile: 'Warzone',
  connectedDevices: [],
  leftStickX: 0,
  leftStickY: 0,
  rightStickX: 0,
  rightStickY: 0,
  leftTrigger: 0,
  rightTrigger: 0,
  rawButtons: {},
  outputButtons: {}
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];

function send(type, value) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({ type, value });
  }
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function pct(value) {
  return `${Math.round(clamp(value, 0, 1) * 100)}%`;
}

function render(next) {
  Object.assign(state, next);

  $('#connectionStatus').textContent = state.connectionStatus;
  $('#connectionText').textContent = state.connectionStatus;
  $('#activeProfile').textContent = state.activeProfileName;
  $('#footerProfile').textContent = state.activeProfileName;
  $('#footerStatus').textContent = state.isRunning ? state.connectionStatus : 'Ready';
  $('#toggleConnection').textContent = state.isConnecting
    ? 'Working...'
    : state.isRunning ? 'Stop' : 'Start';
  $('#toggleConnection').disabled = state.isConnecting;
  $('#connectionPill').classList.toggle('online', state.isRunning);

  $('#gameProfile').value = state.gameProfile;

  const devices = state.connectedDevices ?? [];
  $('#deviceList').innerHTML = devices.length
    ? devices.map((device) => `<div>${escapeHtml(device)}</div>`).join('')
    : 'No devices connected';

  renderStick('#leftStick', state.leftStickX, state.leftStickY);
  renderStick('#rightStick', state.rightStickX, state.rightStickY);
  $('#leftStickReadout').innerHTML = `X:${state.leftStickX.toFixed(2)}&nbsp;&nbsp;Y:${state.leftStickY.toFixed(2)}`;
  $('#rightStickReadout').innerHTML = `X:${state.rightStickX.toFixed(2)}&nbsp;&nbsp;Y:${state.rightStickY.toFixed(2)}`;

  $('#leftTrigger').style.height = pct(state.leftTrigger);
  $('#rightTrigger').style.height = pct(state.rightTrigger);
  $('#leftTriggerReadout').textContent = pct(state.leftTrigger);
  $('#rightTriggerReadout').textContent = pct(state.rightTrigger);

  renderButtons('raw-dpad', state.rawButtons);
  renderButtons('raw-buttons', state.rawButtons);
  renderButtons('out-dpad', state.outputButtons);
  renderButtons('out-buttons', state.outputButtons);
  setActive('[data-key="lb"]', state.rawButtons?.lb);
  setActive('[data-key="rb"]', state.rawButtons?.rb);
}

function renderStick(selector, x, y) {
  const max = 44;
  $(selector).style.transform =
    `translate(${clamp(x, -1, 1) * max}px, ${clamp(-y, -1, 1) * max}px)`;
}

function renderButtons(group, buttons = {}) {
  $$(`[data-group="${group}"] [data-key]`).forEach((element) => {
    const key = element.dataset.key;
    element.classList.toggle('active', Boolean(buttons[key]));
  });
}

function setActive(selector, active) {
  $$(selector).forEach((element) => element.classList.toggle('active', Boolean(active)));
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

$$('.nav-item').forEach((button) => {
  button.addEventListener('click', () => {
    $$('.nav-item').forEach((item) => item.classList.remove('active'));
    button.classList.add('active');
    const view = button.dataset.view;
    $$('.view').forEach((panel) => {
      panel.classList.toggle('active', panel.dataset.viewPanel === view);
    });
  });
});

$('#toggleConnection').addEventListener('click', () => send('toggleConnection'));
$('#gameProfile').addEventListener('change', (event) => send('setGameProfile', event.target.value));

window.chrome?.webview?.addEventListener('message', (event) => {
  if (event.data?.type === 'state') {
    render(event.data.payload);
  }
});

render(state);
send('ready');
