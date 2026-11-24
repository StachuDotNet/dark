const $ = id => document.getElementById(id);
const AGENTS = ['codex', 'claude'], LANGUAGES = ['Dark', 'Python', 'TypeScript'];
const AGENT_LABEL = { codex: 'Codex', claude: 'Claude' };
const STORE = 'benchmark.ui.v1';
let modelChoices = { codex: [], claude: [] };
let serverVersion = null, defaults, token, store, latest = { batches: [], busy: false, active: null }, selected = null, rendered = {}, pending = null, openRun = null;

// ---------- small helpers ----------
function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(props)) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v; else if (k.startsWith('on') || k in el) el[k] = v; else el.setAttribute(k, v);
  }
  el.append(...children.flat().filter(c => c != null && c !== false));
  return el;
}
let toastTimer;
function toast(text, error = false) {
  const el = $('toast'); el.textContent = text; el.className = error ? 'toast error' : 'toast'; el.hidden = false;
  clearTimeout(toastTimer); toastTimer = setTimeout(() => { el.hidden = true; }, error ? 8000 : 4000);
}
async function request(url, value, retry = true) {
  const response = await fetch(url, value === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Benchmark-Token': token }, body: JSON.stringify(value) });
  const body = await response.json();
  if (response.status === 403 && body.error === 'Invalid request token' && value !== undefined && retry) {
    // A restart changes the token. The rejected action never ran, so retry it once.
    const fresh = await request('/api/defaults');
    token = fresh.token;
    return request(url, value, false);
  }
  if (!response.ok) throw Error(body.error);
  return body;
}
const guard = action => async event => { try { await action(event); } catch (e) { toast(e.message, true); } };
const uid = () => Math.random().toString(36).slice(2, 10);
const display = n => n == null ? '—' : Number(n).toLocaleString(undefined, { maximumFractionDigits: 4 });
function duration(ms) {
  if (ms == null) return '—';
  const s = Math.round(ms / 1000); if (s < 60) return `${(ms / 1000).toFixed(1)}s`;
  const m = Math.floor(s / 60); if (m < 60) return m < 10 && s % 60 ? `${m}m ${s % 60}s` : `${m} min`;
  return `${Math.floor(m / 60)}h ${m % 60}m`;
}
function analysisAgents(config) {
  return AGENTS.map(agent => config.agents.find(a => a.agent === agent) || defaults.agents.find(a => a.agent === agent)).filter(a => a?.path);
}
async function viewReport(b, agent) {
  const report = await request('/api/report', { batch: b.key, agent });
  renderReport(report.text);
  $('reportTitle').textContent = `Dark analysis report · ${AGENT_LABEL[agent]}`;
  $('reportMeta').textContent = `${b.name} · Separate analysis session · ${duration(report.analysis.durationMs)} · Cost $${display(report.analysis.usage?.costUsd)}`;
  $('reportDialog').showModal();
}
function renderReport(text) {
  const lines = text.trim().split(/\r?\n/).filter(line => line.trim());
  // Display simple new reports as a readable list; keep older report formats readable too.
  if (lines.length && lines.every(line => /^\s*[-*]\s+/.test(line))) {
    $('reportText').replaceChildren(h('ul', {}, ...lines.map(line => h('li', {}, line.replace(/^\s*[-*]\s+/, '')))));
  } else $('reportText').replaceChildren(h('pre', {}, text));
}
function darkComparisonText(dark, summaries, metric) {
  const peers = summaries.filter(s => s.language !== 'Dark' && s.task === dark.task && s.agent === dark.agent && s.model === dark.model && s.status === dark.status && s.setupTrack === dark.setupTrack && s.verification === dark.verification);
  const value = dark.medians[metric];
  if (typeof value !== 'number' || !Number.isFinite(value) || (metric === 'durationMs' && value <= 0)) return [];
  return peers.filter(p => p.medians[metric] > 0).map(peer => {
    const ratio = value / peer.medians[metric];
    const multiple = Number(ratio.toPrecision(3));
    if (multiple === 1) return `Same as ${peer.language}`;
    const amount = multiple.toLocaleString(undefined, { maximumSignificantDigits: 3 });
    if (metric === 'durationMs') return ratio > 1
      ? `${amount}× as long as ${peer.language}`
      : `${Number((1 / ratio).toPrecision(3))}× faster than ${peer.language}`;
    return metric === 'costUsd' ? `${amount}× the cost of ${peer.language}` : `${amount}× the tokens of ${peer.language}`;
  });
}
const time = iso => iso ? new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '—';
const plural = (n, word) => `${n} ${word}${n === 1 ? '' : 's'}`;
const get = (obj, path) => path.split('.').reduce((o, k) => o?.[k], obj);
function set(obj, path, value) { const keys = path.split('.'), last = keys.pop(); keys.reduce((o, k) => o[k], obj)[last] = value; }
const STATUS_LABEL = { waiting: 'Queued', queued: 'Queued', preparing: 'Preparing' };
const TONE = { completed: 'ok', passed: 'ok', finished: 'ok', running: 'run', preparing: 'run', 'limit reached': 'warn', 'environment blocked': 'bad', failed: 'bad' };
const pill = status => h('span', { class: `pill ${TONE[status] || ''}` }, STATUS_LABEL[status] || status[0].toUpperCase() + status.slice(1));
const agentTag = (agent, model) => h('span', { class: 'who' }, h('span', { class: `tag agent-${agent}` }, AGENT_LABEL[agent]), model && h('span', { class: 'model' }, model));
const CLI_DEFAULT = '(CLI default)';
// Attempts store "(CLI default)" when no model was chosen; show the model that actually ran instead.
// With no attempts to go by (review, queue), use the model the CLI is currently set to use.
function modelLabel(agent, model, runs) {
  if (model && model !== CLI_DEFAULT) return model;
  const used = runs ? runs.find(r => r.agent === agent && r.model === CLI_DEFAULT && r.modelUsed)?.modelUsed : modelChoices.defaults?.[agent];
  return used || 'CLI default';
}
const langTag = (language, suffix = '') => h('span', { class: `tag lang-${language}` }, language + suffix);
const finished = r => !['queued', 'running'].includes(r.status);
// Same rule as the server: a total is shown only when every attempt reported the measurement.
const total = (runs, pick) => runs.length && runs.every(r => typeof pick(r) === 'number') ? runs.reduce((s, r) => s + pick(r), 0) : null;

// ---------- configurations (saved in this browser) ----------
function freshDraft(name) {
  return { key: uid(), name,
    agents: Object.fromEntries(defaults.agents.map(a => [a.agent, { enabled: a.agent === 'codex', path: a.path, model: a.model }])),
    languages: Object.fromEntries(LANGUAGES.map(l => [l, defaults.languages.includes(l)])),
    ...Object.fromEntries(['darkPath', 'darkBuild', 'outputFolder', 'repetitions', 'minutes'].map(k => [k, defaults[k]])),
    documentation: { ...defaults.documentation },
    tasks: defaults.tasks.map(task => taskDraft(task)) };
}
function taskDraft(task) {
  // Keep the user's edits and choices, and drop settings we no longer use.
  const template = defaults.tasks.find(t => t.templateId === task.templateId || t.name === task.name);
  const prompt = template && task.prompt?.trim() === template.prompt.split('\n\n')[0] ? template.prompt : task.prompt || '';

  // Edited as comma-separated text; older saved tasks fall back to the template's sites.
  const sites = task.networkSites ?? template?.networkSites ?? [];
  return { source: task.source, templateId: task.templateId, enabled: task.enabled !== false,
    name: task.name || '', prompt, networkSites: Array.isArray(sites) ? sites.join(', ') : String(sites) };
}
const configTasks = c => c.tasks ?? (c.task ? [c.task] : []);
function normalize(d) {
  const base = freshDraft(d.name || 'Untitled');
  return { ...base, ...Object.fromEntries(Object.keys(base).filter(k => d[k] !== undefined).map(k => [k, d[k]])),
    tasks: (d.tasks ?? base.tasks).map(taskDraft),
    agents: Object.fromEntries(AGENTS.map(a => [a, { ...base.agents[a], ...d.agents?.[a] }])),
    languages: { ...base.languages, ...d.languages }, documentation: { Dark: d.documentation?.Dark || '' } };
}

function fromConfig(c, name) {
  const d = freshDraft(name);
  for (const a of AGENTS) { const found = c.agents.find(x => x.agent === a); d.agents[a] = found ? { enabled: true, path: found.path, model: found.model } : { ...d.agents[a], enabled: false }; }
  for (const l of LANGUAGES) d.languages[l] = c.languages.includes(l);
  for (const k of ['darkPath', 'darkBuild', 'outputFolder', 'repetitions', 'minutes']) d[k] = c[k];
  Object.assign(d, { documentation: { ...d.documentation, ...c.documentation }, tasks: configTasks(c).map(taskDraft) });
  return d;
}
function toConfig(d) {
  return { ...defaults, name: d.name.trim() || 'Untitled batch',
    agents: AGENTS.filter(a => d.agents[a].enabled).map(a => ({ agent: a, path: d.agents[a].path.trim(), model: d.agents[a].model.trim() })),
    languages: LANGUAGES.filter(l => d.languages[l]),
    ...Object.fromEntries(['darkPath', 'darkBuild', 'outputFolder'].map(k => [k, String(d[k] ?? '').trim()])),
    workspaceRoot: defaults.workspaceRoot,
    ...Object.fromEntries(['repetitions', 'minutes'].map(k => [k, Number(d[k])])),
    documentation: { Dark: d.documentation?.Dark || '' },
    tasks: d.tasks.filter(task => task.enabled).map(task => ({ source: task.source, templateId: task.templateId, name: task.name.trim(), prompt: task.prompt,
      networkSites: String(task.networkSites || '').split(/[\s,]+/).filter(Boolean) })) };
}
// Quick client-side checks so problems show up while editing. The server still validates everything.
function issues(d) {
  const out = [], add = (section, text) => out.push({ section, text });
  const agents = AGENTS.filter(a => d.agents[a].enabled);
  if (!agents.length) add('agents', 'Enable at least one agent.');
  for (const a of agents) {
    if (!d.agents[a].path.trim()) add('agents', `Set the ${AGENT_LABEL[a]} executable.`);
    if (d.agents[a].modelCustom && !d.agents[a].model.trim()) add('agents', `Enter the ${AGENT_LABEL[a]} custom model name.`);
  }
  if (!LANGUAGES.some(l => d.languages[l])) add('languages', 'Enable at least one language.');
  if (d.languages.Dark && !d.darkPath.trim()) add('languages', 'Set the Dark executable.');
  const tasks = d.tasks.filter(task => task.enabled), names = new Set();
  if (!tasks.length) add('task', 'Include at least one task.');
  for (const task of tasks) {
    const name = task.name.trim();
    if (!name || !task.prompt.trim()) add('task', 'Add a name and prompt for each included task.');
    if (names.has(name)) add('task', 'Give each included task a different name.');
    names.add(name);
  }
  for (const [k, label, min, max, int] of [['repetitions', 'Runs per task', 1, 100, true], ['minutes', 'Time limit per run (minutes)', 0.01, 1440, false]]) {
    const n = Number(d[k]); if (d[k] === '' || !Number.isFinite(n) || n < min || n > max || (int && !Number.isInteger(n))) add('limits', `${label} must be ${int ? 'a whole number ' : ''}from ${min} to ${max}.`);
  }
  if (!String(d.outputFolder).startsWith('/')) add('environment', 'Use an absolute output folder path.');
  return out;
}
function shape(d) {
  const agents = AGENTS.filter(a => d.agents[a].enabled), languages = LANGUAGES.filter(l => d.languages[l]);
  const reps = Number(d.repetitions) || 0;
  const tasks = d.tasks.filter(task => task.enabled);
  return { agents, languages, tasks, reps, attempts: tasks.length * agents.length * languages.length * reps };
}
const draft = () => store.drafts.find(d => d.key === store.selected) ?? store.drafts[0];
function persist() { try { localStorage.setItem(STORE, JSON.stringify(store)); } catch {} }
function loadStore() {
  let saved = null; try { saved = JSON.parse(localStorage.getItem(STORE)); } catch {}
  if (Array.isArray(saved?.drafts) && saved.drafts.length) return { ...saved, drafts: saved.drafts.map(normalize), open: saved.open ?? {} };
  const initial = freshDraft('Configuration 1');
  return { drafts: [initial], selected: initial.key, open: { agents: true, languages: true } };
}

// ---------- sidebar ----------
function renderConfigList() {
  $('configList').replaceChildren(...store.drafts.map(d => {
    const s = shape(d);
    const meta = `${s.agents.map(a => AGENT_LABEL[a]).join(' + ') || 'No agent'} · ${s.languages.join(', ') || 'no language'} · ${plural(s.attempts, 'attempt')}`;
    return h('li', { class: `config-item${d.key === store.selected ? ' selected' : ''}` },
      h('button', { class: 'config-select', type: 'button', title: d.name, onclick: () => selectDraft(d.key) }, h('strong', {}, d.name || 'Untitled'), h('span', {}, meta)),
      h('div', { class: 'config-tools' },
        h('button', { class: 'icon', type: 'button', title: 'Duplicate', 'aria-label': `Duplicate ${d.name}`, onclick: () => addDraft({ ...structuredClone(d), key: uid(), name: `${d.name} (copy)` }) }, '⧉'),
        h('button', { class: 'icon', type: 'button', title: 'Delete', 'aria-label': `Delete ${d.name}`, disabled: store.drafts.length === 1, onclick: () => deleteDraft(d) }, '✕')));
  }));
}
function selectDraft(key) { store.selected = key; persist(); fillEditor(); }
function addDraft(d) { d = normalize(d); store.drafts.push(d); selectDraft(d.key); $('editor').querySelector('[data-bind=name]').select(); }
function deleteDraft(d) {
  if (!confirm(`Delete the configuration “${d.name}”? Batches already started are not affected.`)) return;
  store.drafts = store.drafts.filter(x => x !== d);
  if (store.selected === d.key) store.selected = store.drafts[0].key;
  persist(); fillEditor();
}
function renderTasks() {
  $('taskList').replaceChildren(...draft().tasks.map((task, index) => {
    const bind = (tag, field, props = {}) => h(tag, { ...props, 'data-bind': `tasks.${index}.${field}` });
    const remove = h('button', { type: 'button', class: 'ghost small', onclick: () => {
      draft().tasks.splice(index, 1); persist(); fillEditor();
    }, 'aria-label': `Remove ${task.name || 'task'}` }, 'Remove');
    return h('div', { class: 'toggle-card', 'data-task-index': index },
      h('label', { class: 'toggle' }, bind('input', 'enabled', { type: 'checkbox' }), h('span', {}, `Include task ${index + 1}`)),
      h('div', { class: 'toggle-body' },
        h('label', {}, 'Task name', bind('input', 'name')),
        h('label', {}, 'Shared prompt', bind('textarea', 'prompt', { rows: 8 })),
        h('p', { class: 'hint' }, 'Ask the agent to test its program in this prompt. The runner does not run extra checks.'),
        h('label', {}, 'Network sites', bind('input', 'networkSites', { class: 'mono', placeholder: 'None — the task runs offline' })),
        h('p', { class: 'hint' }, 'Comma-separated host names both agents may reach, e.g. api.github.com. Every other site stays blocked. Not shown to the agent.')),
      h('div', { class: 'task-actions' }, remove));
  }));
}
function modelSelection(value, choices, custom = false) {
  return custom || (value && !choices.some(choice => choice.value === value)) ? '__custom__' : value;
}
function renderModels() {
  for (const agent of AGENTS) {
    const settings = draft().agents[agent], choices = modelChoices[agent] || [];
    const select = document.querySelector(`[data-model-agent="${agent}"]`);
    select.replaceChildren(h('option', { value: '' }, modelLabel(agent, '', null)), ...choices.map(choice => h('option', { value: choice.value }, choice.label)), h('option', { value: '__custom__' }, 'Custom model…'));
    select.value = modelSelection(settings.model, choices, settings.modelCustom);
    settings.modelCustom = select.value === '__custom__';
    document.querySelector(`[data-custom-model-agent="${agent}"]`).hidden = !settings.modelCustom;
  }
}
function fillEditor() {
  const d = draft();
  renderTasks();
  renderModels();
  for (const el of $('editor').querySelectorAll('[data-bind]')) {
    const value = get(d, el.dataset.bind);
    if (el.type === 'checkbox') el.checked = !!value; else el.value = value ?? '';
  }
  refreshSidebar();
}
function refreshSidebar() {
  const d = draft(), problems = issues(d), s = shape(d);
  renderConfigList();
  for (const card of document.querySelectorAll('[data-agent]')) card.classList.toggle('on', d.agents[card.dataset.agent].enabled);
  for (const card of document.querySelectorAll('[data-language]')) card.classList.toggle('on', d.languages[card.dataset.language]);
  const meta = {
    agents: s.agents.map(a => AGENT_LABEL[a] + (d.agents[a].model.trim() ? ` (${d.agents[a].model.trim()})` : '')).join(', ') || 'None',
    languages: s.languages.join(', ') || 'None',
    task: `${s.tasks.length} of ${d.tasks.length} included`,
    limits: `${d.repetitions} runs · ${d.minutes} min/run`,
    environment: String(d.outputFolder).split('/').filter(Boolean).pop() || '—' };
  for (const section of document.querySelectorAll('.section')) {
    const name = section.dataset.section, problem = problems.find(p => p.section === name);
    section.classList.toggle('has-issue', !!problem);
    section.querySelector('[data-meta]').textContent = problem ? problem.text : meta[name];
  }
  const worst = s.attempts * (Number(d.minutes) || 0) * 60000;
  $('estimate').replaceChildren(h('strong', {}, plural(s.attempts, 'attempt')),
    ` · ${plural(s.tasks.length, 'task')} × ${s.agents.length} agents × ${s.languages.length} languages × ${plural(s.reps, 'rep')}${s.attempts ? ` · at most ${duration(worst)}` : ''}`);
  $('taskCount').textContent = `${s.tasks.length} of ${d.tasks.length} tasks included`;
  document.querySelectorAll('[data-task-index]').forEach(card => {
    const task = d.tasks[card.dataset.taskIndex];
    card.classList.toggle('on', task.enabled);
    card.querySelector('.toggle span').textContent = task.name || `Task ${Number(card.dataset.taskIndex) + 1}`;
  });
  $('issues').replaceChildren(...problems.map(p => h('li', {}, p.text)));
  $('review').disabled = problems.length > 0;
  $('review').textContent = latest.busy ? 'Review & queue' : 'Review & run';
}

// ---------- review & start ----------
async function openReview() {
  const d = draft(); pending = toConfig(d);
  const s = shape(d);
  $('reviewLead').textContent = latest.busy
    ? 'Another session is running. This batch waits in the queue and starts automatically, one after another.'
    : 'The environment is checked first, then attempts run sequentially in shuffled order.';
  const matrix = $('reviewMatrix');
  matrix.style.gridTemplateColumns = `auto repeat(${s.languages.length}, 1fr)`;
  matrix.replaceChildren(h('div', { class: 'head' }), ...s.languages.map(l => h('div', { class: 'head' }, langTag(l))),
    ...pending.agents.flatMap(a => [h('div', { class: 'head' }, agentTag(a.agent, modelLabel(a.agent, a.model, null))), ...s.languages.map(() => h('div', { class: 'cell' }, `${s.tasks.length} tasks × ${s.reps} reps`))]));
  const facts = [
    ['Name', pending.name], ['Tasks', pending.tasks.map(task => task.name).join(', ')], ['Attempts', `${s.attempts} (${s.tasks.length} tasks × ${s.agents.length} agents × ${s.languages.length} languages × ${s.reps} reps)`],
    ['Limits', `${pending.minutes} min per run`],
    ['Network', pending.tasks.some(t => t.networkSites.length) ? pending.tasks.map(t => `${t.name}: ${t.networkSites.join(', ') || 'offline'}`).join(' · ') : 'Offline for every task'], ['Worst case', duration(s.attempts * pending.minutes * 60000)],
    ...pending.agents.map(a => [AGENT_LABEL[a.agent], `${a.path} · ${modelLabel(a.agent, a.model, null)}`]),
    ...(pending.languages.includes('Dark') ? [['Dark', `${pending.darkPath}${pending.darkBuild ? ` · ${pending.darkBuild}` : ''}`]] : []),
    ['Output', pending.outputFolder],
    ['Completion', 'CLI finished without an error. Program correctness is not independently verified.'],
    ['Behavior', 'Fresh project and conversation per attempt; shuffled order; one attempt at a time; no automatic follow-up prompts. Existing agent settings remain active.'],
    ['Timing', 'From initial submission through agent completion, including any tests the agent runs.'],
    ['Cancellation', 'Stops the process group and ordinary children. Deliberately detached children may remain.'],
    ['Human notes', 'Recorded only; never sent to the agent.'],
    ['Usage', 'CLI-reported usage and cost; missing values stay blank. Cached input is included in input totals. Logs redact known credentials.'],
    ['Projects', pending.workspaceRoot] ];
  $('reviewFacts').replaceChildren(...facts.flatMap(([k, v]) => [h('dt', {}, k), h('dd', {}, v)]));
  const preview = await request('/api/preview', pending);
  pending.runtimeId = preview.runtimeId;
  pending.reviewedInstructions = preview.prompts;
  $('reviewPrompts').replaceChildren(...preview.prompts.map(p => h('div', {}, h('h3', {}, `${p.task} · ${p.language} · ${AGENT_LABEL[p.agent]} #${p.repetition}`), h('pre', {}, p.prompt))));
  if (preview.runtime) $('reviewFacts').append(h('dt', {}, 'Fresh Dark runtime'), h('dd', {}, `${preview.runtime.root} — a separate binary copy, empty package store and .NET cache for every Dark attempt; setup happens before timing.`));
  $('reviewCli').textContent = JSON.stringify(preview.cliSettings, null, 2);
  $('analysisInstructions').textContent = preview.analysisPrompt;
  $('reviewJson').textContent = JSON.stringify(pending, null, 2);
  $('confirmStart').textContent = latest.busy ? 'Add to queue' : 'Start batch';
  $('reviewDialog').returnValue = '';
  $('reviewDialog').showModal();
}
async function confirmStart() {
  if ($('reviewDialog').returnValue !== 'start' || !pending) return;
  const config = pending; pending = null;
  toast(latest.busy ? 'Adding to the queue…' : 'Checking the environment and starting…');
  const batch = await request('/api/start', config);
  selected = batch.key;
  toast(batch.status === 'waiting' ? `“${batch.name}” is queued.` : `“${batch.name}” started.`);
  await refresh();
}
async function runPreflight() {
  const button = $('preflight'); button.disabled = true; button.textContent = 'Checking…';
  try {
    const r = await request('/api/preflight', toConfig(draft()));
    const facts = [...r.agents.map(a => [AGENT_LABEL[a.agent], a.version]), ...Object.entries(r.languages).map(([l, v]) => [l, `${v.version}${v.buildType ? ` · ${v.buildType}` : ''}`]),
      ['Instruction files', r.instructions.length ? r.instructions.map(i => i.path).join(', ') : 'None found']];
    $('preflightBody').replaceChildren(h('p', {}, h('span', { class: 'pill ok' }, 'Ready'), ' Versions and help output are saved with each batch.'),
      h('dl', { class: 'facts' }, ...facts.flatMap(([k, v]) => [h('dt', {}, k), h('dd', {}, v)])),
      h('p', { class: 'hint' }, r.caveat),
      h('details', {}, h('summary', {}, 'Full inventory (JSON)'), h('pre', {}, JSON.stringify(r, null, 2))));
    $('preflightDialog').showModal();
  } finally { button.disabled = false; button.textContent = 'Check environment'; }
}

// ---------- batches ----------
function renderBatchList() {
  const waiting = latest.batches.filter(b => b.status === 'waiting');
  $('batchList').replaceChildren(...latest.batches.map(b => {
    const done = b.runs.filter(finished).length;
    const detail = b.status === 'waiting' ? `#${waiting.indexOf(b) + 1} in queue` : b.runs.length ? `${done}/${b.runs.length} attempts` : time(b.queuedAt);
    return h('button', { class: `batch-tab tone-${TONE[b.status] || 'idle'}${b.key === selected ? ' selected' : ''}`, type: 'button', onclick: () => { selected = b.key; openRun = null; paint(); } },
      h('strong', { title: b.name }, b.name), h('div', { class: 'row' }, pill(b.status), h('small', {}, detail)),
      h('div', { class: 'mini-progress' }, h('div')));
  }));
  // The page's security policy blocks inline styles, so set widths through the stylesheet API.
  [...$('batchList').children].forEach((tab, i) => { const b = latest.batches[i]; tab.querySelector('.mini-progress div').style.width = `${b.runs.length ? 100 * b.runs.filter(finished).length / b.runs.length : 0}%`; });
  const active = latest.batches.find(b => b.key === latest.active);
  const engine = $('engine');
  engine.className = `pill ${active ? 'run' : ''}`;
  engine.textContent = active ? `Running · ${active.name}${waiting.length ? ` · ${waiting.length} queued` : ''}` : latest.busy ? 'Busy' : 'Idle';
}
function renderBatch(b) {
  const isActive = b.key === latest.active, changed = rendered.key !== b.key;
  const done = b.runs.filter(finished), current = b.runs.find(r => r.status === 'running');
  $('batchName').replaceChildren(b.name, pill(b.status));
  const c = b.config, known = b.runs.length ? b.runs : null;
  const matrix = `${plural(configTasks(c).length, 'task')} × ${c.agents.map(a => `${AGENT_LABEL[a.agent]} (${modelLabel(a.agent, a.model, known)})`).join(' + ')} × ${c.languages.join(', ')} × ${plural(c.repetitions, 'rep')}`;
  $('batchSub').textContent = b.startedAt ? `Started ${time(b.startedAt)}${b.finishedAt ? ` · finished ${time(b.finishedAt)}` : ''} · ${matrix}` : `Queued ${time(b.queuedAt)} · ${matrix}`;
  const cancel = $('cancel');
  cancel.hidden = !(b.status === 'waiting' || (isActive && ['running', 'preparing'].includes(b.status)));
  cancel.textContent = b.status === 'waiting' ? 'Remove from queue' : 'Cancel batch';
  $('deleteBatch').hidden = ['waiting', 'running', 'preparing'].includes(b.status) || isActive || b.analysis?.status === 'running';
  $('deleteBatch').disabled = !!b.deleting;
  $('batchProgress').hidden = !['waiting', 'running', 'preparing'].includes(b.status);
  $('batchProgress').textContent = $('batchProgress').hidden ? '' : b.progress || '';
  $('batchError').hidden = !b.error; $('batchError').textContent = b.error || '';
  renderByLanguage(b, done);
  $('batchFolder').replaceChildren(...(b.folder ? [['Exports', b.folder], ['Final projects', c.workspaceRoot]].map(([k, v]) => h('div', { class: 'path-row' }, h('span', { class: 'path-label' }, k), h('code', { class: 'path', title: v }, v))) : []));
  $('totalsCard').hidden = $('byLanguage').hidden && !b.folder;

  $('runs').replaceChildren(...(b.runs.length ? [...b.runs].sort((a, b) => (a.durationMs ?? Infinity) - (b.durationMs ?? Infinity)).map((run, i) => h('tr', { class: `clickable${run.status === 'running' ? ' current' : ''}`, title: run.id, onclick: () => showRun(b, run.id) },
    h('td', { class: 'muted' }, i + 1), h('td', {}, run.task || configTasks(c)[0]?.name || '—'), h('td', {}, agentTag(run.agent, modelLabel(run.agent, run.model, [run]))), h('td', {}, langTag(run.language, ` #${run.repetition}`)), h('td', {}, pill(run.status)),
    ...[run.usage.inputTokens, run.usage.outputTokens, run.usage.modelCalls].map(v => h('td', { class: 'num' }, display(v))),
    h('td', { class: 'num' }, run.userPrompts), h('td', { class: 'num' }, display(run.usage.costUsd)), h('td', { class: 'num' }, duration(run.durationMs))))
    : [h('tr', {}, h('td', { class: 'empty-row', colSpan: 11 }, b.status === 'waiting' ? 'Attempts appear when this batch starts.' : b.status === 'preparing' ? 'Checking the environment…' : 'No attempts.'))]));

  const comparisons = b.summaries.filter(s => s.language === 'Dark' && ['completed', 'passed'].includes(s.status));
  $('summaryCard').hidden = !b.summaries.length;
  $('summary').replaceChildren(...(comparisons.length ? comparisons.map(s => h('tr', {},
    h('td', {}, h('div', {}, s.task || '—'), h('small', { class: 'muted' }, agentTag(s.agent, modelLabel(s.agent, s.model, b.runs)))),
    ...['inputTokens', 'outputTokens', 'costUsd', 'durationMs'].map(metric => {
      const differences = darkComparisonText(s, b.summaries, metric);
      return h('td', { class: 'compare' }, ...(differences.length ? differences.map(text => h('div', {}, text)) : ['—']));
    }))) : [h('tr', {}, h('td', { class: 'empty-row', colSpan: 5 }, 'Comparisons appear when Dark and another language have comparable completed sessions.'))]));

  const log = $('events'), activitySnapshot = JSON.stringify(b.events);
  if (changed || rendered.activity !== activitySnapshot) {
    const stick = changed || log.scrollTop + log.clientHeight >= log.scrollHeight - 8;
    log.replaceChildren(...(b.events.length ? b.events.map(event => {
      const run = b.runs.find(r => r.id === event.runId);
      return h('article', { class: `activity-item activity-${event.kind || 'output'}` },
        h('div', { class: 'activity-meta' }, h('strong', {}, event.title || 'CLI output'), h('span', {}, `${event.at ? time(event.at) : 'Saved activity'} · ${run ? `${AGENT_LABEL[run.agent]} · ${run.language} · ${run.task} #${run.repetition}` : 'Session'}`)),
        event.text && h('div', { class: 'activity-text' }, event.text),
        event.details && h('details', {}, h('summary', {}, 'Command'), h('pre', {}, event.details)));
    }) : [h('p', { class: 'muted' }, current ? 'Waiting for agent activity…' : 'No saved activity was found. Original logs are in each attempt’s artifacts folder.')]));
    rendered.activity = activitySnapshot;
    if (stick) log.scrollTop = log.scrollHeight;
  }
  const canNote = isActive && !!current;
  $('note').disabled = !canNote; $('record').disabled = !canNote;
  $('note').placeholder = canNote ? `Attached to the running attempt (${current.agent} / ${current.language})` : 'Available while an attempt in this batch is running.';

  if (changed) {
    // Either agent can write a report, even if only one of them took part in the batch.
    $('analysisAgent').replaceChildren(...analysisAgents(c).map(a => h('option', { value: a.agent }, `${AGENT_LABEL[a.agent]} · ${modelLabel(a.agent, a.model, null)}`)));
    $('batchConfig').textContent = JSON.stringify(c, null, 2);
  }
  const hasDark = b.runs.some(r => r.language === 'Dark');
  const reports = AGENTS.map(agent => b.analyses?.[agent]).filter(Boolean);
  $('viewReports').replaceChildren(...reports.filter(r => r.hasReport).map(r => h('button', { type: 'button', class: 'secondary', onclick: guard(() => viewReport(b, r.agent.agent)) }, `View ${AGENT_LABEL[r.agent.agent]} report`)));
  $('analyze').disabled = !b.frozen || latest.busy || !hasDark;
  const analysisNote = !hasDark && b.runs.length ? 'This batch has no Dark attempts.' : !b.frozen ? 'Available once results are frozen.' : latest.busy ? 'Available when the current session finishes.' : '';
  const running = b.analysis?.status === 'running' ? [h('div', { class: 'path-row' }, h('span', { class: 'path-label' }, 'New report'), pill('running'))] : [];
  $('analysisStatus').replaceChildren(...(running.length || reports.length
    ? [...running, ...reports.flatMap(r => [h('div', { class: 'path-row' }, h('span', { class: 'path-label' }, `${AGENT_LABEL[r.agent.agent]} report`), pill(r.status)),
       h('div', { class: 'path-row' }, h('span', { class: 'path-label' }, 'Saved to'), h('code', { class: 'path', title: r.folder }, r.folder))])]
    : analysisNote ? [h('p', { class: 'hint' }, analysisNote)] : []));
  rendered.key = b.key;
  if (openRun) showRun(b, openRun, true);
}
// Split the batch totals by language (and by agent when there is more than one), using the same rules as the totals.
function renderByLanguage(b, done) {
  const c = b.config, multiAgent = c.agents.length > 1;
  const groups = c.languages.flatMap(language => c.agents.map(a => ({ language, agent: a.agent, model: a.model || '(CLI default)' })));
  const runsOf = g => b.runs.filter(r => r.language === g.language && (!multiAgent || (r.agent === g.agent && r.model === g.model)));
  const show = b.runs.length && groups.length > (multiAgent ? 1 : 0) && (c.languages.length > 1 || multiAgent);
  $('byLanguage').hidden = !show;
  if (!show) return;
  const rows = (multiAgent ? groups : c.languages.map(language => ({ language }))).map(g => {
    const runs = runsOf(g), finishedRuns = runs.filter(r => done.includes(r));
    const passed = runs.filter(r => ['completed', 'passed'].includes(r.status)).length;
    return h('tr', {},
      h('td', {}, langTag(g.language)), multiAgent && h('td', {}, agentTag(g.agent, modelLabel(g.agent, g.model, b.runs))),
      h('td', { class: 'num' }, `${passed}/${runs.length}`),
      h('td', { class: 'num' }, display(total(finishedRuns, r => r.usage.inputTokens))),
      h('td', { class: 'num' }, display(total(finishedRuns, r => r.usage.outputTokens))),
      h('td', { class: 'num' }, display(total(finishedRuns, r => r.usage.costUsd))),
      h('td', { class: 'num' }, duration(total(finishedRuns, r => r.durationMs))));
  });
  $('byLanguage').replaceChildren(h('table', {},
    h('thead', {}, h('tr', {}, h('th', {}, 'By language'), multiAgent && h('th', {}, 'Agent'), ...['Completed', 'Input tokens', 'Output tokens', 'Cost $', 'Time'].map(t => h('th', { class: 'num' }, t)))),
    h('tbody', {}, ...rows)));
}
function showRun(b, id, refreshOnly = false) {
  const run = b.runs.find(r => r.id === id);
  if (!run) return;
  const snapshot = JSON.stringify(run);
  if (refreshOnly && (!$('runDialog').open || rendered.run === snapshot)) return;
  openRun = id; rendered.run = snapshot;
  const facts = (rows) => h('dl', { class: 'facts' }, ...rows.flatMap(([k, v]) => [h('dt', {}, k), h('dd', { class: /^(Workspace|Artifacts|Run ID)$/.test(k) ? 'mono' : '' }, v ?? '—')]));
  const u = run.usage;
  $('runTitle').textContent = `${run.task || 'Task'} · ${AGENT_LABEL[run.agent]} / ${modelLabel(run.agent, run.model, [run])} · ${run.language} #${run.repetition}`;
  const body = [h('p', {}, pill(run.status)), run.error && h('p', { class: 'error-text' }, run.error),
    facts([['Task', run.task], ['Model used', run.modelUsed || (finished(run) ? 'Not reported by the CLI' : null)], ['Run ID', run.id], ['Started', run.startedAt && new Date(run.startedAt).toLocaleString()], ['Duration', duration(run.durationMs)], ['User prompts', run.userPrompts],
      ['Verification', run.verification === 'agent-self-test' ? 'Agent asked to test; no independent checks' : 'Legacy run with acceptance checks'], ['Workspace', run.workspace], ['Artifacts', run.artifactFolder]]),
    h('h3', {}, 'Usage'),
    facts([['Input', display(u.inputTokens)], ['Cached input', display(u.cachedInputTokens)], ['Cache creation', display(u.cacheCreationTokens)], ['Output', display(u.outputTokens)],
      ['Reasoning', display(u.reasoningTokens)], ['Model calls', display(u.modelCalls)], ['Cost $', display(u.costUsd)]])];
  if (run.workspace && finished(run)) body.unshift(h('button', { type: 'button', class: 'secondary', onclick: guard(async () => {
    const project = await request('/api/code', { batch: b.key, run: run.id });
    $('codeTitle').textContent = `Final code · ${run.task} · ${run.language} #${run.repetition}`;
    $('codePath').textContent = project.workspace;
    const select = $('codeFile');
    select.replaceChildren(...project.files.map((file, i) => h('option', { value: String(i) }, file.name)));
    select.disabled = !project.files.length;
    const showFile = () => { $('codeText').textContent = project.files[Number(select.value)]?.content ?? 'No readable source files are available.'; };
    select.onchange = showFile;
    showFile();
    $('codeSkipped').replaceChildren(...project.skipped.map(file => h('li', {}, `${file.name} — ${file.reason}`)));
    const last = project.tries.at(-1);
    $('tryCommand').value = last?.command ?? project.suggestedCommand;
    $('tryInput').value = last?.input ?? '';
    $('tryStatus').textContent = '';
    showTries(project.tries);
    const runIt = guard(async () => {
      $('tryRun').disabled = true; $('tryStatus').textContent = 'Running…';
      try {
        const result = await request('/api/try', { batch: b.key, run: run.id, command: $('tryCommand').value, input: $('tryInput').value });
        $('tryStatus').textContent = ''; showTries(result.tries);
      } catch (error) { $('tryStatus').textContent = ''; throw error; } finally { $('tryRun').disabled = false; }
    });
    $('tryRun').onclick = runIt;
    // Enter would otherwise submit the dialog form and close it.
    $('tryCommand').onkeydown = event => { if (event.key === 'Enter') { event.preventDefault(); runIt(); } };
    $('codeDialog').showModal();
  }) }, 'Check code'));
  if (run.instructions) body.push(h('h3', {}, 'Exact instructions sent'), h('pre', {}, run.instructions));
  if (run.interventions.length) body.push(h('h3', {}, 'Human notes (not sent to the agent)'), h('ul', { class: 'check-list' }, ...run.interventions.map(n => h('li', {}, h('span', { class: 'muted' }, time(n.at)), n.text))));
  body.push(h('details', {}, h('summary', {}, 'Raw attempt record (JSON)'), h('pre', {}, JSON.stringify(run, null, 2))));
  $('runBody').replaceChildren(...body.filter(Boolean));
  if (!$('runDialog').open) $('runDialog').showModal();
}
// Newest first; the result of each manual run sits under the code it ran.
function showTries(tries) {
  $('tryResults').replaceChildren(...tries.slice().reverse().map((t, i) => {
    const outcome = t.timedOut ? 'Timed out' : t.error ? 'Could not start' : `Exit code ${t.exitCode}`;
    const ok = !t.timedOut && !t.error && t.exitCode === 0;
    const body = [h('pre', {}, `$ ${t.command}${t.input ? `\n[stdin] ${t.input}` : ''}`),
      h('h4', {}, 'Output'), h('pre', {}, t.stdout || '(no output)'),
      t.stderr && h('h4', {}, 'Errors'), t.stderr && h('pre', { class: 'try-stderr' }, t.stderr),
      t.error && h('p', { class: 'error-text' }, t.error)];
    return h('details', { class: 'try-result', open: i === 0 },
      h('summary', {}, h('span', { class: `pill ${ok ? 'ok' : t.timedOut ? 'warn' : 'bad'}` }, outcome), ` ${duration(t.durationMs)} · ${time(t.at)} · `, h('code', {}, t.command)), ...body);
  }));
}
function paint() {
  const batches = latest.batches;
  if (!batches.some(b => b.key === selected)) selected = latest.active ?? batches.at(-1)?.key ?? null;
  renderBatchList();
  const b = batches.find(x => x.key === selected);
  $('empty').hidden = !!b; $('batchView').hidden = !b;
  if (b) renderBatch(b);
  $('review').textContent = latest.busy ? 'Review & queue' : 'Review & run';
}
let refreshing = false;
async function refresh() {
  if (refreshing) return; refreshing = true;
  try {
    latest = await request('/api/state');
    // An open tab keeps running old page code after a server update; reload so it sends current settings.
    if (serverVersion && latest.version !== serverVersion) {
      if (![...document.querySelectorAll('dialog')].some(d => d.open)) return location.reload();
      toast('The dashboard was updated. Close this window and the page will reload.');
    }
    serverVersion ||= latest.version;
    paint();
  } finally { refreshing = false; }
}

// ---------- wiring ----------
function closeOnOutsideClick(dialog) {
  const outside = event => {
    const box = dialog.getBoundingClientRect();
    return event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom;
  };
  let startedOutside = false;
  dialog.addEventListener('pointerdown', event => { startedOutside = event.button === 0 && outside(event); });
  dialog.addEventListener('click', event => {
    // A drag that starts inside should not dismiss the popup. Review dismissal never starts a run.
    if (startedOutside && outside(event)) dialog.close('cancel');
    startedOutside = false;
  });
}
async function init() {
  document.querySelectorAll('dialog').forEach(closeOnOutsideClick);
  const data = await request('/api/defaults'); defaults = data.config; token = data.token; modelChoices = data.modelChoices || modelChoices; $('analysisInstructions').textContent = data.analysisPrompt;
  store = loadStore();
  if (!store.drafts.some(d => d.key === store.selected)) store.selected = store.drafts[0].key;
  for (const section of document.querySelectorAll('.section')) {
    if (section.dataset.section in store.open) section.open = store.open[section.dataset.section];
    section.addEventListener('toggle', () => { store.open[section.dataset.section] = section.open; persist(); });
  }
  fillEditor();
  $('editor').addEventListener('change', event => {
    const agent = event.target.dataset.modelAgent;
    if (!agent) return;
    const settings = draft().agents[agent];
    settings.modelCustom = event.target.value === '__custom__';
    settings.model = settings.modelCustom ? '' : event.target.value;
    persist(); fillEditor();
  });
  $('editor').addEventListener('input', event => {
    const el = event.target; if (!el.dataset.bind) return;
    set(draft(), el.dataset.bind, el.type === 'checkbox' ? el.checked : el.value);
    persist(); refreshSidebar();
  });
  $('editor').addEventListener('submit', event => event.preventDefault());
  $('addTask').onclick = () => {
    const tasks = draft().tasks; let number = tasks.length + 1;
    while (tasks.some(task => task.name === `task-${number}`)) number++;
    tasks.push(taskDraft({ ...defaults.tasks[0], templateId: undefined, name: `task-${number}`, source: undefined, enabled: true }));
    persist(); fillEditor();
  };
  $('newConfig').onclick = () => addDraft(freshDraft(`Configuration ${store.drafts.length + 1}`));
  $('review').onclick = guard(openReview);
  $('reviewDialog').addEventListener('close', () => guard(confirmStart)());
  $('preflight').onclick = guard(runPreflight);
  $('runDialog').addEventListener('close', () => { openRun = null; rendered.run = null; });
  $('deleteBatch').onclick = guard(async () => {
    const b = latest.batches.find(x => x.key === selected);
    if (!confirm(`Delete the entire batch “${b.name}” and all ${b.runs.length} attempts? This permanently removes its saved results, logs, reports and generated projects. Your sidebar configuration will be kept.`)) return;
    await request('/api/delete-batch', { batch: b.key });
    openRun = null;
    for (const id of ['runDialog', 'codeDialog', 'reportDialog']) if ($(id).open) $(id).close();
    selected = null;
    toast('Batch deleted.');
    await refresh();
  });
  $('cancel').onclick = guard(async () => {
    const b = latest.batches.find(x => x.key === selected);
    if (b.status !== 'waiting' && !confirm('Cancel this batch? The running CLI is stopped and remaining attempts are recorded as cancelled.')) return;
    await request('/api/cancel', { batch: b.key }); toast(b.status === 'waiting' ? 'Removed from the queue.' : 'Cancelling…'); await refresh();
  });
  $('record').onclick = guard(async () => { await request('/api/note', { text: $('note').value }); $('note').value = ''; toast('Human note recorded.'); });
  $('copyConfig').onclick = () => { const b = latest.batches.find(x => x.key === selected); addDraft(fromConfig(b.config, `${b.name} (edit)`)); toast('Copied into a new configuration on the left.'); };
  $('analyze').onclick = guard(async () => {
    const b = latest.batches.find(x => x.key === selected);
    const agent = analysisAgents(b.config).find(a => a.agent === $('analysisAgent').value);
    toast('Starting a separate analysis session…');
    const pendingAnalysis = request('/api/analyze', { ...agent, batch: b.key });
    await refresh(); await pendingAnalysis; toast('Analysis session finished.'); await refresh();
  });
  setInterval(() => refresh().catch(e => toast(e.message, true)), 1500);
  await refresh();
}
init().catch(e => toast(e.message, true));
