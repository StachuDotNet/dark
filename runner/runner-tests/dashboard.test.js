import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
import { defaults } from '../src/config.js';

// Test saved configuration conversion without a browser or paid agent session.
test('current configurations retain edits and selections while obsolete fields are dropped', async () => {
  const source = (await readFile(new URL('../public/app.js', import.meta.url), 'utf8')).replace(/init\(\)\.catch\([^\n]+\);\s*$/, '');
  const context = vm.createContext({ seed: defaults });
  vm.runInContext(source, context);
  const value = JSON.parse(vm.runInContext(`
    defaults = seed;
    const example = freshDraft('My saved config');
    for (const agent of AGENTS) example.agents[agent].enabled = true;
    for (const language of LANGUAGES) example.languages[language] = true;
    example.repetitions = 3;
    example.darkPath = '/my/custom/dark';
    example.tasks = [taskDraft({...seed.tasks[1], name:'Edited task', prompt:'My edited prompt', enabled:true}), taskDraft({...seed.tasks[0],enabled:false})];
    example.instructionNotes = 'obsolete';
    example.taskCatalogVersion = 3;
    example.tasks[0].startingFiles = 'invalid old JSON';
    example.tasks[0].checks = 'invalid old JSON';
    example.tasks[0].ready = false;
    example.tasks[0].unavailableReason = 'old';
    const normalized = normalize(example);
    const config = toConfig(normalized);
    JSON.stringify({normalized,config,issues:issues(normalized),attempts:shape(normalized).attempts,copied:fromConfig(config,'Copy')});
  `, context));
  assert.equal(value.attempts, 18);
  assert.deepEqual(value.issues, []);
  assert.equal(value.normalized.name, 'My saved config');
  assert.equal(value.config.darkPath, '/my/custom/dark');
  assert.equal(value.config.tasks[0].prompt, 'My edited prompt');
  assert.equal('checkType' in value.config.tasks[0], false);
  assert.equal(value.normalized.tasks[1].enabled, false);
  assert.equal(value.copied.tasks[0].prompt, 'My edited prompt');
  for (const field of ['startingFiles','checks','ready','unavailableReason']) assert.equal(field in value.config.tasks[0], false);
  for (const field of ['instructionNotes','taskCatalogVersion','darkBinaryVersion']) assert.equal(field in value.normalized, false);
});

test('a stale request token refreshes and retries only once', async () => {
  const source = (await readFile(new URL('../public/app.js', import.meta.url), 'utf8')).replace(/init\(\)\.catch\([^\n]+\);\s*$/, '');
  const calls = [];
  const responses = [
    { status: 403, ok: false, body: { error: 'Invalid request token' } },
    { status: 200, ok: true, body: { token: 'fresh' } },
    { status: 200, ok: true, body: { ok: true } }
  ];
  const context = vm.createContext({ fetch: async (url, options) => {
    calls.push({ url, options });
    const response = responses.shift();
    return { ...response, json: async () => response.body };
  } });
  vm.runInContext(source, context);
  await vm.runInContext("token = 'old'; request('/api/analyze', { batch: 'saved' })", context);
  assert.deepEqual(calls.map(c => c.url), ['/api/analyze', '/api/defaults', '/api/analyze']);
  assert.equal(calls[2].options.headers['X-Benchmark-Token'], 'fresh');
  assert.equal(calls[0].options.body, calls[2].options.body);
  calls.length = 0;
  responses.push(
    { status: 403, ok: false, body: { error: 'Invalid request token' } },
    { status: 200, ok: true, body: { token: 'another' } },
    { status: 403, ok: false, body: { error: 'Invalid request token' } }
  );
  await assert.rejects(vm.runInContext("request('/api/analyze', { batch: 'saved' })", context), /Invalid request token/);
  assert.equal(calls.length, 3);
});


test('every dashboard element used by browser code exists in the page', async () => {
  const source = await readFile(new URL('../public/app.js', import.meta.url), 'utf8');
  const html = await readFile(new URL('../public/index.html', import.meta.url), 'utf8');
  const ids = new Set([...html.matchAll(/id="([^"]+)"/g)].map(m => m[1]));
  const referenced = new Set([...source.matchAll(/\$\('([^']+)'\)/g)].map(m => m[1]));
  assert.deepEqual([...referenced].filter(id => !ids.has(id)), []);
});

test('compact comparisons show only ratios for matching sessions and leave missing usage blank', async () => {
  const source = (await readFile(new URL('../public/app.js', import.meta.url), 'utf8')).replace(/init\(\)\.catch\([^\n]+\);\s*$/, '');
  const row = language => ({task:'t',agent:'codex',model:'m',language,status:'completed',setupTrack:'ready-environment',verification:'agent-self-test',medians:{inputTokens:10,outputTokens:20,costUsd:2,durationMs:1000}});
  const dark = {...row('Dark'),medians:{inputTokens:20,outputTokens:10,costUsd:null,durationMs:2000}};
  const peers = [row('Python'), {...row('TypeScript'),model:'other'}, {...row('TypeScript'),status:'failed'}, {...row('TypeScript'),medians:{inputTokens:0}}];
  const context = vm.createContext({dark, peers}); vm.runInContext(source, context);
  assert.equal(vm.runInContext("darkComparisonText(dark, peers, 'inputTokens').join('; ')", context), '2× the tokens of Python');
  assert.equal(vm.runInContext("darkComparisonText(dark, peers, 'outputTokens').join('; ')", context), '0.5× the tokens of Python');
  assert.equal(vm.runInContext("darkComparisonText(dark, peers, 'costUsd').length", context), 0);
  assert.equal(vm.runInContext("darkComparisonText(dark, peers, 'durationMs').join('; ')", context), '2× as long as Python');
  vm.runInContext('dark.medians.durationMs = 500', context);
  assert.equal(vm.runInContext("darkComparisonText(dark, peers, 'durationMs').join('; ')", context), '2× faster than Python');
});

test('model selectors preserve exact custom names and pass explicit selections to configurations', async () => {
  const source = (await readFile(new URL('../public/app.js', import.meta.url), 'utf8')).replace(/init\(\)\.catch\([^\n]+\);\s*$/, '');
  const context = vm.createContext({ seed: defaults }); vm.runInContext(source, context);
  const result = JSON.parse(vm.runInContext(`
    defaults = seed;
    const choices = [{value:'listed-model',label:'Listed model'}];
    const d = freshDraft('Models');
    d.agents.codex.model = 'my-custom-model';
    const customConfig = toConfig(normalize(d));
    d.agents.codex.model = 'listed-model';
    const selectedConfig = toConfig(d);
    d.agents.codex.model = ''; d.agents.codex.modelCustom = true;
    JSON.stringify({listed:modelSelection('listed-model',choices),custom:modelSelection('my-custom-model',choices),automatic:modelSelection('',choices),explicitCustom:modelSelection('',choices,true),customModel:customConfig.agents[0].model,selectedModel:selectedConfig.agents[0].model,issues:issues(d)});
  `, context));
  assert.equal(result.listed, 'listed-model');
  assert.equal(result.custom, '__custom__');
  assert.equal(result.automatic, '');
  assert.equal(result.explicitCustom, '__custom__');
  assert.equal(result.customModel, 'my-custom-model');
  assert.equal(result.selectedModel, 'listed-model');
  assert.ok(result.issues.some(issue => issue.text.includes('custom model name')));
});
