const buttonOptions = [
  'None', 'A', 'B', 'X', 'Y', 'LeftShoulder', 'RightShoulder',
  'LeftThumb', 'RightThumb', 'Back', 'Start', 'DPadUp', 'DPadDown', 'DPadLeft', 'DPadRight'
];

const triggerOptions = ['None', 'RightTrigger', 'LeftTrigger'];
const macroTypes = [
  'NoRecoil', 'AutoFire', 'AutoPing', 'Remap', 'Sequence', 'Toggle', 'AimAssistBuff',
  'HeadAssist', 'ScriptedShape', 'ProgressiveRecoil', 'TrackingAssist',
  'AutoFireNoRecoil', 'InstaDropShot', 'JumpShot', 'StrafeShot', 'HoldBreath',
  'SlideCancel', 'FastDrop', 'CrowBar', 'Custom'
];
const shapeKinds = ['Flick', 'Circle', 'HorizontalOval', 'VerticalOval', 'DiagonalOval'];
const stickTargets = ['Left', 'Right'];
const easingKinds = ['Linear', 'EaseOutQuad', 'EaseOutCubic', 'EaseInOutSine', 'EaseOutBack', 'Smoothstep'];
const distanceSources = ['TriggerHoldTime', 'AimStickDeflection', 'RecoilMagnitude', 'Manual', 'Auto'];
const crowBarModes = ['Rapido', 'Padrao'];
const scriptTriggerKinds = ['WhileHeld', 'OnPress', 'Toggle'];
const scriptActionKinds = ['PressButton', 'ReleaseButton', 'SetAxis', 'SetTrigger', 'Wait', 'LoopBack', 'LoopStart'];
const analogAxes = ['LeftStickX', 'LeftStickY', 'RightStickX', 'RightStickY', 'LeftTrigger', 'RightTrigger'];
let selectedScriptStepIndex = -1;

const state = {
  isRunning: false,
  isConnecting: false,
  connectionStatus: 'Disconnected',
  activeProfileId: '',
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
  outputButtons: {},
  profiles: [],
  filters: {},
  settings: {},
  logs: [],
  macros: [],
  selectedMacroId: '',
  weaponDetection: {
    isRunning: false,
    currentWeaponName: 'None',
    statusMessage: 'Detection is stopped.',
    captureX: 1700,
    captureY: 950,
    captureWidth: 300,
    captureHeight: 60,
    intervalMs: 250,
    matchThreshold: 0.8,
    weapons: [],
    games: [],
    categories: [],
    selectedGame: '',
    selectedCategory: 'All',
    searchText: '',
    libraryWeapons: []
  }
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
let activeView = 'dashboard';
let hasRenderedAll = false;

function send(type, extra = {}) {
  if (window.chrome?.webview) {
    window.chrome.webview.postMessage({ type, ...extra });
  }
}

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function pct(value) {
  return `${Math.round(clamp(value, 0, 1) * 100)}%`;
}

function fixed(value, digits = 2) {
  return Number(value ?? 0).toFixed(digits);
}

function render(next) {
  Object.assign(state, next);
  renderDashboard();
  if (!hasRenderedAll) {
    renderFilters();
    renderProfiles();
    renderLogs();
    renderSettings();
    renderMacros();
    renderWeapons();
    hasRenderedAll = true;
    return;
  }
  renderActiveView();
}

function renderInputState(next) {
  Object.assign(state, next);
  renderDashboard();
}

function renderActiveView() {
  if (activeView === 'curve') renderFilters();
  if (activeView === 'profiles') renderProfiles();
  if (activeView === 'logs') renderLogs();
  if (activeView === 'settings') renderSettings();
  if (activeView === 'macros') renderMacros();
  if (activeView === 'weapons') renderWeapons();
}

function renderDashboard() {
  $('#connectionStatus').textContent = state.connectionStatus;
  $('#connectionText').textContent = state.connectionStatus;
  $('#activeProfile').textContent = state.activeProfileName;
  $('#footerProfile').textContent = state.activeProfileName;
  $('#footerStatus').textContent = state.isRunning ? state.connectionStatus : 'Ready';
  $('#toggleConnection').textContent = state.isConnecting ? 'Working...' : state.isRunning ? 'Stop' : 'Start';
  $('#toggleConnection').disabled = state.isConnecting;
  $('#connectionPill').classList.toggle('online', state.isRunning);
  setControlValue($('#gameProfile'), state.gameProfile);

  const devices = state.connectedDevices ?? [];
  $('#deviceList').innerHTML = devices.length
    ? devices.map((device) => `<div>${escapeHtml(device)}</div>`).join('')
    : 'No devices connected';

  renderStick('#leftStick', state.leftStickX, state.leftStickY);
  renderStick('#rightStick', state.rightStickX, state.rightStickY);
  $('#leftStickReadout').innerHTML = `X:${fixed(state.leftStickX)}&nbsp;&nbsp;Y:${fixed(state.leftStickY)}`;
  $('#rightStickReadout').innerHTML = `X:${fixed(state.rightStickX)}&nbsp;&nbsp;Y:${fixed(state.rightStickY)}`;
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

function renderFilters() {
  const filters = state.filters ?? {};
  bindRange('leftStickDeadzone', filters.leftStickDeadzone, 2);
  bindRange('rightStickDeadzone', filters.rightStickDeadzone, 2);
  bindRange('leftStickAntiDeadzone', filters.leftStickAntiDeadzone, 2);
  bindRange('rightStickAntiDeadzone', filters.rightStickAntiDeadzone, 2);
  bindRange('triggerDeadzone', filters.triggerDeadzone, 2);
  bindRange('responseCurveExponent', filters.responseCurveExponent, 2);
  bindRange('smoothingFactor', filters.smoothingFactor, 2);
  setControlChecked($('#smoothingEnabled'), Boolean(filters.smoothingEnabled));
  renderCurvePreview();
}

function renderProfiles() {
  const profiles = state.profiles ?? [];
  $('#profilesList').innerHTML = profiles.map((profile) => `
    <article class="profile-card ${profile.isActive ? 'active' : ''}">
      <div class="profile-head">
        <div>
          <strong>${escapeHtml(profile.name)}</strong>
          <span>${profile.macroCount} macros</span>
        </div>
        <div class="profile-actions">
          <button class="mini-action" data-action="activate" data-id="${profile.id}">${profile.isActive ? 'Active' : 'Use'}</button>
          <button class="mini-action" data-action="duplicate" data-id="${profile.id}">Copy</button>
          <button class="mini-action" data-action="export" data-id="${profile.id}">Export</button>
          ${profile.isDefault ? '' : `<button class="mini-action danger" data-action="delete" data-id="${profile.id}">Delete</button>`}
        </div>
      </div>
      <div class="process-list">
        ${(profile.associatedProcesses?.length
          ? profile.associatedProcesses.map((process) => `
              <span class="process-chip">
                ${escapeHtml(process)}
                <button data-action="remove-process" data-id="${profile.id}" data-value="${escapeAttribute(process)}">×</button>
              </span>`).join('')
          : '<span class="empty-copy">No linked processes</span>')}
      </div>
      <div class="process-entry">
        <input type="text" id="process-${profile.id}" placeholder="Add process name" />
        <button class="mini-action" data-action="add-process" data-id="${profile.id}">Add</button>
      </div>
    </article>`).join('');
}

function renderLogs() {
  const logs = state.logs ?? [];
  $('#logsList').innerHTML = logs.length
    ? logs.map((entry) => `
      <div class="log-row">
        <span class="log-time">${escapeHtml(entry.timestamp)}</span>
        <span class="log-level level-${String(entry.level).toLowerCase()}">${escapeHtml(entry.level)}</span>
        <span class="log-category">${escapeHtml(entry.category)}</span>
        <span class="log-message">${escapeHtml(entry.message)}</span>
      </div>`).join('')
    : '<div class="empty-copy">No log entries in the current session view.</div>';
  $('#logsList').scrollTop = $('#logsList').scrollHeight;
}

function renderSettings() {
  const settings = state.settings ?? {};
  setControlValue($('#pollingRateMs'), settings.pollingRateMs ?? 1);
  setControlValue($('#logLevel'), settings.logLevel ?? 'Information');
  setControlChecked($('#minimizeToTray'), Boolean(settings.minimizeToTray));
  setControlChecked($('#startMinimized'), Boolean(settings.startMinimized));
  setControlChecked($('#autoConnect'), Boolean(settings.autoConnect));
  setControlChecked($('#showNotifications'), Boolean(settings.showNotifications));
}

function renderMacros() {
  const macros = state.macros ?? [];
  const selected = getSelectedMacro();

  $('#macroList').innerHTML = macros.length
    ? macros.map((macro) => `
      <button class="macro-item ${macro.id === state.selectedMacroId ? 'active' : ''}" data-id="${macro.id}">
        <span class="macro-item-bar ${macro.enabled ? 'enabled' : ''}"></span>
        <span class="macro-item-copy">
          <strong>${escapeHtml(macro.name)}</strong>
          <small>${escapeHtml(macro.type)}</small>
        </span>
      </button>`).join('')
    : '<div class="empty-copy macro-empty-copy">No macros yet. Create one to begin.</div>';

  $('#macroEmpty').classList.toggle('hidden', Boolean(selected));
  $('#macroEditorBody').classList.toggle('hidden', !selected);
  if (!selected) return;

  $('#macroEditorTitle').textContent = selected.name || 'Macro Settings';
  populateSelect('#macroType', macroTypes, selected.type);
  populateSelect('#activationButton', buttonOptions, selected.activationButton);
  populateSelect('#triggerSource', triggerOptions, selected.triggerSource);
  populateSelect('#pingButton', buttonOptions, selected.pingButton);
  populateSelect('#sourceButton', buttonOptions, selected.sourceButton);
  populateSelect('#targetButton', buttonOptions, selected.targetButton);
  populateSelect('#crouchButton', buttonOptions, selected.crouchButton);
  populateSelect('#jumpButton', buttonOptions, selected.jumpButton);
  populateSelect('#breathButton', buttonOptions, selected.breathButton);
  populateSelect('#slideButton', buttonOptions, selected.slideButton);
  populateSelect('#slideCancelButton', buttonOptions, selected.slideCancelButton);

  setControlValue($('#macroName'), selected.name);
  setControlValue($('#macroPriority'), selected.priority);
  setControlChecked($('#macroEnabled'), selected.enabled);
  setControlChecked($('#toggleMode'), selected.toggleMode);
  setControlChecked($('#macroLoop'), selected.loop);
  setControlValue($('#macroDelayMs'), selected.delayMs);
  setControlValue($('#macroIntervalMs'), selected.intervalMs);
  setControlValue($('#macroIntensity'), selected.intensity);
  $('#macroIntensityValue').textContent = fixed(selected.intensity, 2);
  setControlValue($('#macroRandomization'), selected.randomizationFactor);
  $('#macroRandomizationValue').textContent = fixed(selected.randomizationFactor, 2);
  setControlValue($('#recoilCompensationX'), selected.recoilCompensationX);
  setControlValue($('#recoilCompensationY'), selected.recoilCompensationY);
  setControlValue($('#flickStrength'), selected.flickStrength);
  setControlValue($('#flickIntervalMs'), selected.flickIntervalMs);
  setControlValue($('#jumpIntervalMs'), selected.jumpIntervalMs);
  setControlValue($('#slideCancelDelayMs'), selected.slideCancelDelayMs);
  setControlValue($('#strafeAmplitude'), selected.strafeAmplitude);
  $('#strafeAmplitudeValue').textContent = fixed(selected.strafeAmplitude, 2);
  setControlValue($('#strafeIntervalMs'), selected.strafeIntervalMs);

  populateSelect('#motionShape', shapeKinds, selected.motion?.shape);
  populateSelect('#motionTarget', stickTargets, selected.motion?.target);
  populateSelect('#motionEasing', easingKinds, selected.motion?.easing);
  setControlValue($('#motionRadiusX'), selected.motion?.radiusXNorm);
  setControlValue($('#motionRadiusY'), selected.motion?.radiusYNorm);
  setControlValue($('#motionRotation'), selected.motion?.rotationDeg);
  setControlValue($('#motionPeriodMs'), selected.motion?.periodMs);
  setControlValue($('#motionDurationMs'), selected.motion?.durationMs);
  setControlValue($('#motionStartPhase'), selected.motion?.startPhaseDeg);
  setControlValue($('#motionDirection'), selected.motion?.directionDeg);
  setControlValue($('#motionAmplitude'), selected.motion?.amplitudeNorm);
  setControlValue($('#motionIntensityMul'), selected.motion?.intensityMul);
  setControlChecked($('#motionClockwise'), selected.motion?.clockwise);
  setControlChecked($('#motionAdditive'), selected.motion?.additive);

  populateSelect('#taTarget', stickTargets, selected.trackingAssist?.target);
  populateSelect('#taShape', shapeKinds, selected.trackingAssist?.shape);
  populateSelect('#taEasing', easingKinds, selected.trackingAssist?.easing);
  setControlValue($('#taBaseRadius'), selected.trackingAssist?.baseRadiusNorm);
  setControlValue($('#taMaxRadius'), selected.trackingAssist?.maxRadiusNorm);
  setControlValue($('#taPeriodMs'), selected.trackingAssist?.periodMs);
  setControlValue($('#taDeflectionThreshold'), selected.trackingAssist?.deflectionThreshold);
  setControlValue($('#taScaleCurve'), selected.trackingAssist?.scaleCurve);
  setControlValue($('#taIntensityMul'), selected.trackingAssist?.intensityMul);
  setControlChecked($('#taClockwise'), selected.trackingAssist?.clockwise);
  setControlChecked($('#taFreeOrbit'), selected.trackingAssist?.freeOrbit);

  const ha = selected.headAssist ?? {};
  populateSelect('#haTarget', stickTargets, ha.shortRange?.target);
  populateSelect('#haDistanceSource', distanceSources, ha.distanceSource);
  populateSelect('#haCycleButton', buttonOptions, ha.cycleButton);
  setControlChecked($('#haAdditive'), ha.shortRange?.additive);
  setControlValue($('#haShortAmp'), ha.shortRange?.amplitudeNorm);
  setControlValue($('#haShortDur'), ha.shortRange?.durationMs);
  setControlValue($('#haShortDir'), ha.shortRange?.directionDeg);
  setControlValue($('#haMediumAmp'), ha.mediumRange?.amplitudeNorm);
  setControlValue($('#haMediumDur'), ha.mediumRange?.durationMs);
  setControlValue($('#haMediumDir'), ha.mediumRange?.directionDeg);
  setControlValue($('#haLongAmp'), ha.longRange?.amplitudeNorm);
  setControlValue($('#haLongDur'), ha.longRange?.durationMs);
  setControlValue($('#haLongDir'), ha.longRange?.directionDeg);
  setControlValue($('#haShortHoldMsMax'), ha.shortHoldMsMax);
  setControlValue($('#haMediumHoldMsMax'), ha.mediumHoldMsMax);
  setControlValue($('#haDeflectionShortMax'), ha.deflectionShortMax);
  setControlValue($('#haDeflectionMediumMax'), ha.deflectionMediumMax);
  setControlValue($('#haRecoilShortMax'), ha.recoilShortMax);
  setControlValue($('#haRecoilMediumMax'), ha.recoilMediumMax);
  setControlValue($('#haWeightTrigger'), ha.weightTrigger);
  setControlValue($('#haWeightDeflection'), ha.weightDeflection);
  setControlValue($('#haWeightRecoil'), ha.weightRecoil);
  setControlValue($('#haReFireCooldownMs'), ha.reFireCooldownMs);
  setControlValue($('#haMinTriggerHoldMs'), ha.minTriggerHoldMs);
  setControlChecked($('#haFireOnPress'), ha.fireOnPress);
  setControlChecked($('#haFireOnce'), ha.fireOnce);

  const pr = selected.progressiveRecoil ?? {};
  populateSelect('#prPhaseEasing', easingKinds, pr.phaseEasing);
  setControlValue($('#prTotalAmmo'), pr.totalAmmo);
  setControlValue($('#prFullMagDurationMs'), pr.fullMagDurationMs);
  setControlValue($('#prStartCompX'), pr.startCompX);
  setControlValue($('#prStartCompY'), pr.startCompY);
  setControlValue($('#prMidCompX'), pr.midCompX);
  setControlValue($('#prMidCompY'), pr.midCompY);
  setControlValue($('#prEndCompX'), pr.endCompX);
  setControlValue($('#prEndCompY'), pr.endCompY);
  setControlValue($('#prNoiseFactor'), pr.noiseFactor);
  setControlValue($('#prSensitivityScale'), pr.sensitivityScale);

  const cb = selected.crowBar ?? {};
  populateSelect('#cbMode', crowBarModes, cb.mode);
  setControlValue($('#cbBaseHtg'), cb.baseHtgValue);
  setControlValue($('#cbAssistFactor'), cb.assistFactor);
  setControlValue($('#cbDeflectionThreshold'), cb.deflectionThreshold);
  setControlValue($('#cbDeflectionCurve'), cb.deflectionCurve);
  setControlValue($('#cbMaxCompensation'), cb.maxCompensation);
  setControlValue($('#cbNoiseFactor'), cb.noiseFactor);
  setControlValue($('#cbHtgScalePadrao'), cb.htgScalePadrao);

  const sd = selected.script ?? { steps: [] };
  populateSelect('#scriptTriggerMode', scriptTriggerKinds, sd.triggerMode);
  setControlValue($('#scriptSpeedMultiplier'), sd.speedMultiplier);
  setControlChecked($('#scriptAutoLoop'), sd.autoLoop);
  setControlValue($('#scriptDescription'), sd.description);
  renderScriptSteps(sd.steps ?? []);

  const type = selected.type;
  syncMacroTypePanels(type);
}

function renderScriptSteps(steps) {
  const list = $('#scriptStepList');
  list.innerHTML = steps.length
    ? steps.map((step, i) => `
      <button type="button" class="script-step ${i === selectedScriptStepIndex ? 'active' : ''} ${step.disabled ? 'disabled' : ''}" data-index="${i}">
        <span class="step-index">#${i}</span>
        <span class="step-action">${escapeHtml(step.action)}</span>
        <span class="step-detail">${escapeHtml(formatStepDetail(step))}</span>
      </button>`).join('')
    : '<div class="empty-copy">Nenhum step ainda. Use os botões acima para adicionar.</div>';

  if (selectedScriptStepIndex < 0 || selectedScriptStepIndex >= steps.length) {
    selectedScriptStepIndex = steps.length > 0 ? 0 : -1;
  }
  renderScriptStepEditor(steps[selectedScriptStepIndex]);
}

function formatStepDetail(step) {
  switch (step.action) {
    case 'PressButton':
    case 'ReleaseButton':
      return step.button || '';
    case 'SetAxis':
    case 'SetTrigger':
      return `${step.axis ?? ''} = ${step.value ?? 0}`;
    case 'Wait':
      return `${step.durationMs ?? 0} ms`;
    case 'LoopBack':
      return `→ #${step.loopTargetIndex ?? 0} x${step.repeatCount ?? 0}`;
    case 'LoopStart':
      return `x${step.repeatCount ?? 0}`;
    default:
      return step.label || '';
  }
}

function renderScriptStepEditor(step) {
  const empty = $('#scriptStepEditorEmpty');
  const body = $('#scriptStepEditorBody');
  if (!step) {
    empty.classList.remove('hidden');
    body.classList.add('hidden');
    return;
  }
  empty.classList.add('hidden');
  body.classList.remove('hidden');

  populateSelect('#scriptStepAction', scriptActionKinds, step.action);
  populateSelect('#scriptStepButton', buttonOptions, step.button);
  populateSelect('#scriptStepAxis', ['None', ...analogAxes], step.axis);
  setControlValue($('#scriptStepValue'), step.value);
  setControlValue($('#scriptStepDuration'), step.durationMs);
  setControlValue($('#scriptStepLoopTarget'), step.loopTargetIndex);
  setControlValue($('#scriptStepRepeatCount'), step.repeatCount);
  setControlValue($('#scriptStepLabel'), step.label);
  setControlChecked($('#scriptStepDisabled'), step.disabled);

  syncScriptStepEditor(step.action);
}

function syncScriptStepEditor(action) {
  const showButton = ['PressButton', 'ReleaseButton'].includes(action);
  const showAxis = ['SetAxis', 'SetTrigger'].includes(action);
  const showValue = showAxis;
  const showDuration = action === 'Wait';
  const showLoopTarget = action === 'LoopBack';
  const showRepeatCount = ['LoopBack', 'LoopStart'].includes(action);

  $$('#scriptStepEditorBody [data-step-show="button"]').forEach((el) => el.classList.toggle('hidden', !showButton));
  $$('#scriptStepEditorBody [data-step-show="axis"]').forEach((el) => el.classList.toggle('hidden', !showAxis));
  $$('#scriptStepEditorBody [data-step-show="value"]').forEach((el) => el.classList.toggle('hidden', !showValue));
  $$('#scriptStepEditorBody [data-step-show="duration"]').forEach((el) => el.classList.toggle('hidden', !showDuration));
  $$('#scriptStepEditorBody [data-step-show="loopTarget"]').forEach((el) => el.classList.toggle('hidden', !showLoopTarget));
  $$('#scriptStepEditorBody [data-step-show="repeatCount"]').forEach((el) => el.classList.toggle('hidden', !showRepeatCount));
}

function renderWeapons() {
  const detection = state.weaponDetection ?? {};
  $('#toggleWeaponDetection').textContent = detection.isRunning ? 'Stop Detection' : 'Start Detection';
  $('#currentWeaponName').textContent = detection.currentWeaponName || 'None';
  $('#weaponStatusMessage').textContent = detection.statusMessage || 'Detection is stopped.';
  setControlValue($('#weaponCaptureX'), detection.captureX ?? 1700);
  setControlValue($('#weaponCaptureY'), detection.captureY ?? 950);
  setControlValue($('#weaponCaptureWidth'), detection.captureWidth ?? 300);
  setControlValue($('#weaponCaptureHeight'), detection.captureHeight ?? 60);
  setControlValue($('#weaponIntervalMs'), detection.intervalMs ?? 250);
  setControlValue($('#weaponMatchThreshold'), detection.matchThreshold ?? 0.8);
  $('#weaponMatchThresholdValue').textContent = fixed(detection.matchThreshold, 2);

  populateSelect('#weaponGame', detection.games ?? [], detection.selectedGame);
  populateSelect('#weaponCategory', detection.categories ?? [], detection.selectedCategory);
  setControlValue($('#weaponSearch'), detection.searchText ?? '');
  $('#weaponPreviewPanel').classList.toggle('hidden', !detection.previewImageDataUrl);
  $('#weaponPreviewImage').src = detection.previewImageDataUrl || '';
  $('#weaponPreviewTitle').textContent = detection.previewTitle || 'Region Preview';
  $('#weaponTestPanel').classList.toggle('hidden', !detection.testCaptureResult);
  $('#weaponTestResult').textContent = detection.testCaptureResult || '';

  $('#weaponLibraryList').innerHTML = (detection.libraryWeapons ?? []).map((weapon) => `
    <article class="library-item">
      <div class="library-item-main">
        <span class="library-chip">${escapeHtml(weapon.category)}</span>
        <strong>${escapeHtml(weapon.name)}</strong>
        <small>Recoil Y ${weapon.recoilCompensationY} - Intensity ${fixed(weapon.intensity, 1)}x</small>
      </div>
      <div class="toolbar-inline">
        <button class="mini-action" data-library-action="activate" data-id="${weapon.id}">Activate</button>
        <button class="mini-action" data-library-action="import" data-id="${weapon.id}">Add</button>
      </div>
    </article>`).join('') || '<div class="empty-copy">No library weapon matches this filter.</div>';

  $('#weaponConfigList').innerHTML = (detection.weapons ?? []).map((weapon) => `
    <article class="weapon-card">
      <div class="weapon-main-row">
        <label class="field fieldless">
          <input data-weapon-field="name" data-id="${weapon.id}" type="text" value="${escapeAttribute(weapon.name)}" />
        </label>
        <label class="field fieldless">
          <input data-weapon-field="recoilCompensationX" data-id="${weapon.id}" type="number" value="${weapon.recoilCompensationX}" />
        </label>
        <label class="field fieldless">
          <input data-weapon-field="recoilCompensationY" data-id="${weapon.id}" type="number" value="${weapon.recoilCompensationY}" />
        </label>
        <label class="field fieldless">
          <input data-weapon-field="intensity" data-id="${weapon.id}" type="number" step="0.05" min="0" max="5" value="${weapon.intensity}" />
        </label>
        <button class="mini-action danger weapon-remove" data-weapon-action="remove" data-id="${weapon.id}">Delete</button>
      </div>
      <div class="weapon-subrow">
        <div class="toolbar-inline">
          <button class="mini-action weapon-reference" data-weapon-action="capture-ref" data-id="${weapon.id}">Capturar Referencia</button>
          ${weapon.referenceCount ? `<button class="mini-action danger" data-weapon-action="clear-refs" data-id="${weapon.id}">Clear</button>` : ''}
        </div>
        <span class="weapon-reference-copy">${weapon.referenceCount === 0 ? 'No reference captured' : `${weapon.referenceCount} references saved`}</span>
        <div class="toolbar-inline weapon-rapid-group">
          <label class="toggle-row fieldless">
            <span>Rapid Fire</span>
            <input data-weapon-field="rapidFireEnabled" data-id="${weapon.id}" type="checkbox" ${weapon.rapidFireEnabled ? 'checked' : ''} />
          </label>
          <label class="field fieldless weapon-rapid-input ${weapon.rapidFireEnabled ? '' : 'hidden'}">
            <span>ms</span>
            <input data-weapon-field="rapidFireIntervalMs" data-id="${weapon.id}" type="number" min="1" max="1000" value="${weapon.rapidFireIntervalMs}" />
          </label>
        </div>
      </div>
      <div class="weapon-custom-region">
        <div class="weapon-custom-top">
          <label class="toggle-row fieldless">
            <span>Usar regiao de captura propria desta arma</span>
            <input data-weapon-field="useCustomRegion" data-id="${weapon.id}" type="checkbox" ${weapon.useCustomRegion ? 'checked' : ''} />
          </label>
          <div class="toolbar-inline ${weapon.useCustomRegion ? '' : 'hidden'}" data-weapon-custom-tools="${weapon.id}">
            <button class="mini-action" data-weapon-action="pick-region" data-id="${weapon.id}">Selecionar</button>
            <button class="mini-action" data-weapon-action="preview-region" data-id="${weapon.id}">Preview</button>
          </div>
        </div>
        <div class="weapon-region-grid ${weapon.useCustomRegion ? '' : 'hidden'}" data-weapon-custom-grid="${weapon.id}">
          <label class="field fieldless">
            <span>X</span>
            <input data-weapon-field="captureX" data-id="${weapon.id}" type="number" value="${weapon.captureX}" />
          </label>
          <label class="field fieldless">
            <span>Y</span>
            <input data-weapon-field="captureY" data-id="${weapon.id}" type="number" value="${weapon.captureY}" />
          </label>
          <label class="field fieldless">
            <span>W</span>
            <input data-weapon-field="captureWidth" data-id="${weapon.id}" type="number" value="${weapon.captureWidth}" />
          </label>
          <label class="field fieldless">
            <span>H</span>
            <input data-weapon-field="captureHeight" data-id="${weapon.id}" type="number" value="${weapon.captureHeight}" />
          </label>
        </div>
      </div>
    </article>`).join('') || '<div class="empty-copy">No configured weapons yet.</div>';
}

function bindRange(id, value, digits) {
  const input = $(`#${id}`);
  const label = $(`#${id}Value`);
  setControlValue(input, value ?? 0);
  label.textContent = fixed(value, digits);
}

function populateSelect(selector, options, selected) {
  const element = $(selector);
  const html = options.map((option) => `<option value="${escapeAttribute(option)}">${escapeHtml(option)}</option>`).join('');
  if (element.dataset.rendered !== html) {
    element.innerHTML = html;
    element.dataset.rendered = html;
  }
  element.value = selected ?? options[0] ?? '';
}

function setControlValue(element, value) {
  if (!element || document.activeElement === element) return;
  element.value = value ?? '';
}

function setControlChecked(element, checked) {
  if (!element || document.activeElement === element) return;
  element.checked = Boolean(checked);
}

function getSelectedMacro() {
  return (state.macros ?? []).find((macro) => macro.id === state.selectedMacroId) ?? null;
}

function collectFilters() {
  return {
    leftStickDeadzone: Number($('#leftStickDeadzone').value),
    rightStickDeadzone: Number($('#rightStickDeadzone').value),
    leftStickAntiDeadzone: Number($('#leftStickAntiDeadzone').value),
    rightStickAntiDeadzone: Number($('#rightStickAntiDeadzone').value),
    triggerDeadzone: Number($('#triggerDeadzone').value),
    responseCurveExponent: Number($('#responseCurveExponent').value),
    smoothingFactor: Number($('#smoothingFactor').value),
    smoothingEnabled: $('#smoothingEnabled').checked
  };
}

function collectSettings() {
  return {
    pollingRateMs: Number($('#pollingRateMs').value),
    logLevel: $('#logLevel').value,
    minimizeToTray: $('#minimizeToTray').checked,
    startMinimized: $('#startMinimized').checked,
    autoConnect: $('#autoConnect').checked,
    showNotifications: $('#showNotifications').checked
  };
}

function collectMacro() {
  const selected = getSelectedMacro();
  if (!selected) return null;
  const haAdditive = $('#haAdditive').checked;
  return {
    id: selected.id,
    name: $('#macroName').value,
    macroType: $('#macroType').value,
    enabled: $('#macroEnabled').checked,
    priority: Number($('#macroPriority').value),
    activationButton: $('#activationButton').value,
    triggerSource: $('#triggerSource').value,
    toggleMode: $('#toggleMode').checked,
    loop: $('#macroLoop').checked,
    delayMs: Number($('#macroDelayMs').value),
    intervalMs: Number($('#macroIntervalMs').value),
    intensity: Number($('#macroIntensity').value),
    randomizationFactor: Number($('#macroRandomization').value),
    recoilCompensationX: Number($('#recoilCompensationX').value),
    recoilCompensationY: Number($('#recoilCompensationY').value),
    pingButton: $('#pingButton').value,
    sourceButton: $('#sourceButton').value,
    targetButton: $('#targetButton').value,
    flickStrength: Number($('#flickStrength').value),
    flickIntervalMs: Number($('#flickIntervalMs').value),
    crouchButton: $('#crouchButton').value,
    jumpButton: $('#jumpButton').value,
    jumpIntervalMs: Number($('#jumpIntervalMs').value),
    strafeAmplitude: Number($('#strafeAmplitude').value),
    strafeIntervalMs: Number($('#strafeIntervalMs').value),
    breathButton: $('#breathButton').value,
    slideButton: $('#slideButton').value,
    slideCancelDelayMs: Number($('#slideCancelDelayMs').value),
    slideCancelButton: $('#slideCancelButton').value,
    motion: {
      shape: $('#motionShape').value,
      target: $('#motionTarget').value,
      radiusXNorm: Number($('#motionRadiusX').value),
      radiusYNorm: Number($('#motionRadiusY').value),
      rotationDeg: Number($('#motionRotation').value),
      periodMs: Number($('#motionPeriodMs').value),
      durationMs: Number($('#motionDurationMs').value),
      directionDeg: Number($('#motionDirection').value),
      amplitudeNorm: Number($('#motionAmplitude').value),
      startPhaseDeg: Number($('#motionStartPhase').value),
      clockwise: $('#motionClockwise').checked,
      easing: $('#motionEasing').value,
      intensityMul: Number($('#motionIntensityMul').value),
      additive: $('#motionAdditive').checked
    },
    trackingAssist: {
      shape: $('#taShape').value,
      target: $('#taTarget').value,
      baseRadiusNorm: Number($('#taBaseRadius').value),
      maxRadiusNorm: Number($('#taMaxRadius').value),
      periodMs: Number($('#taPeriodMs').value),
      clockwise: $('#taClockwise').checked,
      deflectionThreshold: Number($('#taDeflectionThreshold').value),
      scaleCurve: Number($('#taScaleCurve').value),
      easing: $('#taEasing').value,
      intensityMul: Number($('#taIntensityMul').value),
      freeOrbit: $('#taFreeOrbit').checked
    },
    headAssist: {
      shortRange: collectHeadRange('Short', haAdditive),
      mediumRange: collectHeadRange('Medium', haAdditive),
      longRange: collectHeadRange('Long', haAdditive),
      distanceSource: $('#haDistanceSource').value,
      shortHoldMsMax: Number($('#haShortHoldMsMax').value),
      mediumHoldMsMax: Number($('#haMediumHoldMsMax').value),
      deflectionShortMax: Number($('#haDeflectionShortMax').value),
      deflectionMediumMax: Number($('#haDeflectionMediumMax').value),
      recoilShortMax: Number($('#haRecoilShortMax').value),
      recoilMediumMax: Number($('#haRecoilMediumMax').value),
      weightTrigger: Number($('#haWeightTrigger').value),
      weightDeflection: Number($('#haWeightDeflection').value),
      weightRecoil: Number($('#haWeightRecoil').value),
      cycleButton: $('#haCycleButton').value,
      reFireCooldownMs: Number($('#haReFireCooldownMs').value),
      minTriggerHoldMs: Number($('#haMinTriggerHoldMs').value),
      fireOnPress: $('#haFireOnPress').checked,
      fireOnce: $('#haFireOnce').checked
    },
    progressiveRecoil: {
      totalAmmo: Number($('#prTotalAmmo').value),
      fullMagDurationMs: Number($('#prFullMagDurationMs').value),
      startCompX: Number($('#prStartCompX').value),
      startCompY: Number($('#prStartCompY').value),
      midCompX: Number($('#prMidCompX').value),
      midCompY: Number($('#prMidCompY').value),
      endCompX: Number($('#prEndCompX').value),
      endCompY: Number($('#prEndCompY').value),
      phaseEasing: $('#prPhaseEasing').value,
      noiseFactor: Number($('#prNoiseFactor').value),
      sensitivityScale: Number($('#prSensitivityScale').value)
    },
    crowBar: {
      mode: $('#cbMode').value,
      baseHtgValue: Number($('#cbBaseHtg').value),
      assistFactor: Number($('#cbAssistFactor').value),
      deflectionThreshold: Number($('#cbDeflectionThreshold').value),
      deflectionCurve: Number($('#cbDeflectionCurve').value),
      maxCompensation: Number($('#cbMaxCompensation').value),
      noiseFactor: Number($('#cbNoiseFactor').value),
      htgScalePadrao: Number($('#cbHtgScalePadrao').value)
    },
    script: {
      triggerMode: $('#scriptTriggerMode').value,
      autoLoop: $('#scriptAutoLoop').checked,
      speedMultiplier: Number($('#scriptSpeedMultiplier').value),
      description: $('#scriptDescription').value,
      steps: (selected.script?.steps ?? []).map((step) => ({ ...step }))
    }
  };
}

function collectHeadRange(prefix, additive) {
  const selected = getSelectedMacro();
  const base = selected?.headAssist?.[`${prefix.toLowerCase()}Range`] ?? {};
  return {
    ...base,
    target: $('#haTarget').value,
    additive,
    amplitudeNorm: Number($(`#ha${prefix}Amp`).value),
    durationMs: Number($(`#ha${prefix}Dur`).value),
    directionDeg: Number($(`#ha${prefix}Dir`).value)
  };
}

function collectWeaponDetection() {
  return {
    captureX: Number($('#weaponCaptureX').value),
    captureY: Number($('#weaponCaptureY').value),
    captureWidth: Number($('#weaponCaptureWidth').value),
    captureHeight: Number($('#weaponCaptureHeight').value),
    intervalMs: Number($('#weaponIntervalMs').value),
    matchThreshold: Number($('#weaponMatchThreshold').value)
  };
}

function collectWeaponCard(id) {
  const get = (field) => $(`[data-weapon-field="${field}"][data-id="${id}"]`);
  const existing = (state.weaponDetection?.weapons ?? []).find((weapon) => weapon.id === id) ?? {};
  return {
    id,
    name: get('name').value,
    recoilCompensationX: Number(get('recoilCompensationX').value),
    recoilCompensationY: Number(get('recoilCompensationY').value),
    intensity: Number(get('intensity').value),
    rapidFireEnabled: get('rapidFireEnabled').checked,
    rapidFireIntervalMs: Number(get('rapidFireIntervalMs').value),
    useCustomRegion: get('useCustomRegion').checked,
    captureX: Number(get('captureX').value),
    captureY: Number(get('captureY').value),
    captureWidth: Number(get('captureWidth').value),
    captureHeight: Number(get('captureHeight').value),
    referenceImagePaths: existing.referenceImagePaths ?? []
  };
}

function collectWeaponSettings() {
  return {
    ...collectWeaponDetection(),
    weapons: (state.weaponDetection?.weapons ?? []).map((weapon) => collectWeaponCard(weapon.id))
  };
}

function syncMacroTypePanels(type = $('#macroType').value) {
  $('#macroNoRecoilCard').classList.toggle('hidden', !['NoRecoil', 'AutoFireNoRecoil'].includes(type));
  $('#macroButtonsCard').classList.toggle('hidden', !['AutoPing', 'Remap', 'HoldBreath'].includes(type));
  $('#macroAimAssistCard').classList.toggle('hidden', !['AimAssistBuff', 'JumpShot', 'SlideCancel'].includes(type));
  $('#macroMovementCard').classList.toggle('hidden', !['InstaDropShot', 'FastDrop', 'JumpShot', 'StrafeShot', 'HoldBreath', 'SlideCancel'].includes(type));
  $('#macroScriptedShapeCard').classList.toggle('hidden', type !== 'ScriptedShape');
  $('#macroTrackingAssistCard').classList.toggle('hidden', type !== 'TrackingAssist');
  $('#macroHeadAssistCard').classList.toggle('hidden', type !== 'HeadAssist');
  $('#macroProgressiveRecoilCard').classList.toggle('hidden', type !== 'ProgressiveRecoil');
  $('#macroCrowBarCard').classList.toggle('hidden', type !== 'CrowBar');
  $('#macroCustomScriptCard').classList.toggle('hidden', type !== 'Custom');
}

function saveSelectedMacro() {
  const macro = collectMacro();
  if (macro) send('saveMacro', macro);
}

function renderStick(selector, x, y) {
  const max = 44;
  $(selector).style.transform = `translate(${clamp(x, -1, 1) * max}px, ${clamp(-y, -1, 1) * max}px)`;
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

function openModal(id) {
  $(`#${id}`)?.classList.remove('hidden');
}

function closeModal(id) {
  $(`#${id}`)?.classList.add('hidden');
}

function renderCurvePreview() {
  const filters = collectFilters();
  const exponent = Math.max(0.01, filters.responseCurveExponent || 1);
  const deadzone = clamp(filters.rightStickDeadzone || filters.leftStickDeadzone || 0, 0, 0.6);
  const anti = clamp(filters.rightStickAntiDeadzone || filters.leftStickAntiDeadzone || 0, 0, 0.8);
  const smooth = filters.smoothingEnabled ? clamp(filters.smoothingFactor || 0, 0, 1) : 0;

  const points = [];
  for (let i = 0; i <= 40; i += 1) {
    const t = i / 40;
    let x = t;
    let y = t <= deadzone ? 0 : (t - deadzone) / (1 - deadzone || 1);
    y = clamp(anti + (1 - anti) * Math.pow(y, exponent), 0, 1);
    y = y * (1 - smooth * 0.15) + t * (smooth * 0.15);
    points.push([28 + x * 274, 190 - y * 162]);
  }
  const d = points.map((point, index) => `${index === 0 ? 'M' : 'L'}${point[0].toFixed(2)},${point[1].toFixed(2)}`).join(' ');
  $('#curvePath').setAttribute('d', d);
  $('#curvePathShadow').setAttribute('d', d);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll('`', '&#096;');
}

$$('.nav-item').forEach((button) => {
  button.addEventListener('click', () => {
    $$('.nav-item').forEach((item) => item.classList.remove('active'));
    button.classList.add('active');
    const view = button.dataset.view;
    activeView = view;
    $$('.view').forEach((panel) => panel.classList.toggle('active', panel.dataset.viewPanel === view));
    renderActiveView();
  });
});

$('#toggleConnection').addEventListener('click', () => send('toggleConnection'));
$('#gameProfile').addEventListener('change', (event) => send('setGameProfile', { value: event.target.value }));
$('#saveFilters').addEventListener('click', () => send('saveFilters', collectFilters()));
$('#resetFilters').addEventListener('click', () => send('resetFilters'));
$('#saveSettings').addEventListener('click', () => send('saveSettings', collectSettings()));
$('#clearLogs').addEventListener('click', () => send('clearLogs'));
$('#createProfile').addEventListener('click', () => {
  const value = $('#newProfileName').value.trim();
  if (!value) return;
  send('createProfile', { value });
  $('#newProfileName').value = '';
});
$('#createMacro').addEventListener('click', () => send('createMacro'));
$('#deleteMacro').addEventListener('click', () => {
  const selected = getSelectedMacro();
  if (selected) send('deleteMacro', { value: selected.id });
});
$('#saveMacro').addEventListener('click', saveSelectedMacro);
$('#macroEnabled').addEventListener('change', saveSelectedMacro);
$('#macroType').addEventListener('change', (event) => syncMacroTypePanels(event.target.value));
$('#importMacros').addEventListener('click', () => send('importMacros'));
$('#exportMacro').addEventListener('click', () => send('exportMacro'));
$('#exportAllMacros').addEventListener('click', () => send('exportAllMacros'));
$('#importProfile').addEventListener('click', () => send('importProfile'));

$('#scriptStepList').addEventListener('click', (event) => {
  const button = event.target.closest('.script-step');
  if (!button) return;
  selectedScriptStepIndex = Number(button.dataset.index);
  const selected = getSelectedMacro();
  renderScriptSteps(selected?.script?.steps ?? []);
});

document.querySelector('#macroCustomScriptCard .script-toolbar')?.addEventListener('click', (event) => {
  const button = event.target.closest('button[data-script-action]');
  if (!button) return;
  const action = button.dataset.scriptAction;
  const selected = getSelectedMacro();
  if (!selected) return;
  selected.script ??= { steps: [], triggerMode: 'WhileHeld', autoLoop: true, speedMultiplier: 1.0, description: '' };
  selected.script.steps ??= [];
  const steps = selected.script.steps;

  if (action === 'add-press') steps.push({ action: 'PressButton', button: 'A', durationMs: 16, value: 0, axis: 'None', loopTargetIndex: 0, repeatCount: 0, label: '', disabled: false });
  if (action === 'add-release') steps.push({ action: 'ReleaseButton', button: 'A', durationMs: 16, value: 0, axis: 'None', loopTargetIndex: 0, repeatCount: 0, label: '', disabled: false });
  if (action === 'add-axis') steps.push({ action: 'SetAxis', button: 'None', axis: 'LeftStickX', value: 0, durationMs: 16, loopTargetIndex: 0, repeatCount: 0, label: '', disabled: false });
  if (action === 'add-wait') steps.push({ action: 'Wait', button: 'None', axis: 'None', value: 0, durationMs: 100, loopTargetIndex: 0, repeatCount: 0, label: '', disabled: false });
  if (action === 'duplicate' && selectedScriptStepIndex >= 0) {
    steps.splice(selectedScriptStepIndex + 1, 0, { ...steps[selectedScriptStepIndex] });
    selectedScriptStepIndex += 1;
  }
  if (action === 'delete' && selectedScriptStepIndex >= 0) {
    steps.splice(selectedScriptStepIndex, 1);
    if (selectedScriptStepIndex >= steps.length) selectedScriptStepIndex = steps.length - 1;
  }
  if (action === 'move-up' && selectedScriptStepIndex > 0) {
    const t = steps[selectedScriptStepIndex - 1];
    steps[selectedScriptStepIndex - 1] = steps[selectedScriptStepIndex];
    steps[selectedScriptStepIndex] = t;
    selectedScriptStepIndex -= 1;
  }
  if (action === 'move-down' && selectedScriptStepIndex >= 0 && selectedScriptStepIndex < steps.length - 1) {
    const t = steps[selectedScriptStepIndex + 1];
    steps[selectedScriptStepIndex + 1] = steps[selectedScriptStepIndex];
    steps[selectedScriptStepIndex] = t;
    selectedScriptStepIndex += 1;
  }
  if (action === 'clear') {
    steps.length = 0;
    selectedScriptStepIndex = -1;
  }
  if (['add-press', 'add-release', 'add-axis', 'add-wait'].includes(action)) {
    selectedScriptStepIndex = steps.length - 1;
  }
  renderScriptSteps(steps);
});

const scriptStepInputs = ['scriptStepAction', 'scriptStepButton', 'scriptStepAxis',
  'scriptStepValue', 'scriptStepDuration', 'scriptStepLoopTarget',
  'scriptStepRepeatCount', 'scriptStepLabel', 'scriptStepDisabled'];

scriptStepInputs.forEach((id) => {
  const el = $(`#${id}`);
  if (!el) return;
  el.addEventListener('change', () => {
    const selected = getSelectedMacro();
    const step = selected?.script?.steps?.[selectedScriptStepIndex];
    if (!step) return;
    step.action = $('#scriptStepAction').value;
    step.button = $('#scriptStepButton').value;
    step.axis = $('#scriptStepAxis').value;
    step.value = Number($('#scriptStepValue').value);
    step.durationMs = Number($('#scriptStepDuration').value);
    step.loopTargetIndex = Number($('#scriptStepLoopTarget').value);
    step.repeatCount = Number($('#scriptStepRepeatCount').value);
    step.label = $('#scriptStepLabel').value;
    step.disabled = $('#scriptStepDisabled').checked;
    if (id === 'scriptStepAction') syncScriptStepEditor(step.action);
    renderScriptSteps(selected.script.steps);
  });
});
$('#toggleWeaponDetection').addEventListener('click', () => send('toggleWeaponDetection'));
$('#addWeapon').addEventListener('click', () => send('addWeapon'));
$('#openWeaponLibrary').addEventListener('click', () => openModal('weaponLibraryModal'));
$('#saveWeaponSettings').addEventListener('click', () => send('saveWeaponSettings', collectWeaponSettings()));
$('#selectWeaponRegion').addEventListener('click', () => send('selectWeaponRegion'));
$('#previewWeaponCapture').addEventListener('click', () => send('previewWeaponCapture', collectWeaponDetection()));
$('#testWeaponCapture').addEventListener('click', () => send('testWeaponCapture', collectWeaponDetection()));
$('#closeWeaponPreview').addEventListener('click', () => send('closeWeaponPreview'));
$('#copyWeaponTest').addEventListener('click', async () => {
  const text = $('#weaponTestResult').textContent;
  if (text) await navigator.clipboard?.writeText(text);
});
$$( '[data-close-modal]' ).forEach((button) => button.addEventListener('click', () => closeModal(button.dataset.closeModal)));

['macroIntensity', 'macroRandomization', 'strafeAmplitude', 'weaponMatchThreshold',
 'leftStickDeadzone', 'rightStickDeadzone', 'leftStickAntiDeadzone', 'rightStickAntiDeadzone',
 'triggerDeadzone', 'responseCurveExponent', 'smoothingFactor'].forEach((id) => {
  $(`#${id}`).addEventListener('input', () => {
    if (id === 'macroIntensity') $('#macroIntensityValue').textContent = fixed($('#macroIntensity').value, 2);
    if (id === 'macroRandomization') $('#macroRandomizationValue').textContent = fixed($('#macroRandomization').value, 2);
    if (id === 'strafeAmplitude') $('#strafeAmplitudeValue').textContent = fixed($('#strafeAmplitude').value, 2);
    if (id === 'weaponMatchThreshold') $('#weaponMatchThresholdValue').textContent = fixed($('#weaponMatchThreshold').value, 2);
    if (id.endsWith('Deadzone') || id.includes('responseCurve') || id === 'smoothingFactor') {
      const label = $(`#${id}Value`);
      if (label) label.textContent = fixed($(`#${id}`).value, 2);
      renderCurvePreview();
    }
  });
});

$('#smoothingEnabled').addEventListener('change', renderCurvePreview);
$('#weaponGame').addEventListener('change', (event) => send('setWeaponGame', { value: event.target.value }));
$('#weaponCategory').addEventListener('change', (event) => send('setWeaponCategory', { value: event.target.value }));
$('#weaponSearch').addEventListener('input', (event) => send('setWeaponSearch', { value: event.target.value }));

$('#profilesList').addEventListener('click', (event) => {
  const button = event.target.closest('button[data-action]');
  if (!button) return;
  const action = button.dataset.action;
  const id = button.dataset.id;
  const value = button.dataset.value;

  if (action === 'activate') send('activateProfile', { value: id });
  if (action === 'duplicate') send('duplicateProfile', { value: id });
  if (action === 'delete') send('deleteProfile', { value: id });
  if (action === 'export') send('exportProfile', { value: id });
  if (action === 'remove-process') send('removeProfileProcess', { profileId: id, value });
  if (action === 'add-process') {
    const input = $(`#process-${id}`);
    const processValue = input.value.trim();
    if (!processValue) return;
    send('addProfileProcess', { profileId: id, value: processValue });
    input.value = '';
  }
});

$('#macroList').addEventListener('click', (event) => {
  const button = event.target.closest('.macro-item');
  if (!button) return;
  send('selectMacro', { value: button.dataset.id });
});

$('#weaponLibraryList').addEventListener('click', (event) => {
  const button = event.target.closest('button[data-library-action]');
  if (!button) return;
  const action = button.dataset.libraryAction;
  const id = button.dataset.id;
  if (action === 'activate') send('activateLibraryWeapon', { value: id });
  if (action === 'import') {
    send('addWeaponFromLibrary', { value: id });
    closeModal('weaponLibraryModal');
  }
});

$('#weaponConfigList').addEventListener('click', (event) => {
  const button = event.target.closest('button[data-weapon-action]');
  if (!button) return;
  const action = button.dataset.weaponAction;
  const id = button.dataset.id;
  if (action === 'remove') send('removeWeapon', { value: id });
  if (action === 'capture-ref') send('captureWeaponReference', { value: id });
  if (action === 'clear-refs') send('clearWeaponReferences', { value: id });
  if (action === 'pick-region') send('selectWeaponCustomRegion', { value: id });
  if (action === 'preview-region') send('previewWeaponCustomRegion', { value: id });
});

$('#weaponConfigList').addEventListener('change', (event) => {
  const input = event.target;
  if (!(input instanceof HTMLInputElement)) return;
  const id = input.dataset.id;
  if (!id) return;

  if (input.dataset.weaponField === 'useCustomRegion') {
    document.querySelector(`[data-weapon-custom-tools="${id}"]`)?.classList.toggle('hidden', !input.checked);
    document.querySelector(`[data-weapon-custom-grid="${id}"]`)?.classList.toggle('hidden', !input.checked);
  }

  if (input.dataset.weaponField === 'rapidFireEnabled') {
    input.closest('.weapon-rapid-group')?.querySelector('.weapon-rapid-input')?.classList.toggle('hidden', !input.checked);
  }
});

window.chrome?.webview?.addEventListener('message', (event) => {
  if (event.data?.type === 'state') {
    render(event.data.payload);
  }
  if (event.data?.type === 'inputState') {
    renderInputState(event.data.payload);
  }
  if (event.data?.type === 'toast') {
    state.logs = [...(state.logs ?? []), {
      timestamp: new Date().toLocaleTimeString(),
      level: event.data.payload?.level ?? 'Error',
      category: 'WebShell',
      message: event.data.payload?.message ?? 'Command failed'
    }];
    if (activeView === 'logs') renderLogs();
  }
});

render(state);
populateSelect('#macroType', macroTypes, 'NoRecoil');
['#activationButton', '#pingButton', '#sourceButton', '#targetButton', '#crouchButton', '#jumpButton', '#breathButton', '#slideButton', '#slideCancelButton']
  .forEach((selector) => populateSelect(selector, buttonOptions, 'None'));
populateSelect('#triggerSource', triggerOptions, 'RightTrigger');
send('ready');
