import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, readFile, mkdir, chmod } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { command } from '../src/process.js';
import { parseUsage } from '../src/adapters.js';
import { previewInstructions } from '../src/instructions.js';
import { defaultTasks } from '../src/tasks.js';
import { access } from 'node:fs/promises';
import { combineUsage, redact, exportsFor, summaries } from '../src/storage.js';
import { Runner } from '../src/runner.js';
import { defaults, validate, selectedTasks } from '../src/config.js';

const temp = () => mkdtemp(path.join(os.tmpdir(), 'benchmark-test-'));
test('usage stays blank when missing and never double counts cache', () => {
  const codex = parseUsage('codex', [{ type: 'turn.completed', usage: { input_tokens: 100, cached_input_tokens: 80, output_tokens: 20 } }]);
  assert.equal(codex.inputTokens, 100); assert.equal(codex.modelCalls, null); assert.equal(codex.reasoningTokens, null);
  const claude = parseUsage('claude', [ { type: 'assistant', message: { id: 'm1' } }, { type: 'assistant', message: { id: 'm1' } }, { type: 'result', usage: { input_tokens: 10, cache_read_input_tokens: 80, cache_creation_input_tokens: 10, output_tokens: 20 }, total_cost_usd: 0.03 } ]);
  assert.equal(claude.inputTokens, 100); assert.equal(claude.modelCalls, 1); assert.equal(claude.costUsd, 0.03);
  assert.equal(combineUsage([codex, parseUsage('codex', [])]).inputTokens, null);
});
test('cancellation kills the agent process group, including a child', async () => {
  const controller = new AbortController();
  const result = command(process.execPath, ['-e', `const {spawn}=require('node:child_process'); const c=spawn(process.execPath,['-e','setInterval(()=>{},1000)'],{stdio:'ignore'}); console.log(c.pid); setInterval(()=>{},1000);`], { signal: controller.signal, onLine: () => controller.abort() });
  const value = await result; assert.equal(value.cancelled, true);
  const pid = Number(value.stdout.trim());
  await new Promise(resolve => setTimeout(resolve, 100));
  assert.throws(() => process.kill(pid, 0));
});
test('both CLI adapters run single sessions without acceptance checks or follow-ups', async () => {
  const root = await temp();
  const fake = path.join(root, 'fake-agent');
  // This fake CLI lets us exercise the runner without spending anyone's agent quota.
  await writeFile(fake, `#!${process.execPath}
const fs=require('node:fs'); const args=process.argv.slice(2);
if(args.includes('--help')) { console.log('--json --sandbox --skip-git-repo-check --add-dir --output-format --permission-prompts acceptEdits --settings --allowedTools'); process.exit(0); }
if(args.includes('--version')) { console.log('fixture 1.0'); process.exit(0); }
let prompt=''; process.stdin.on('data',x=>prompt+=x); process.stdin.on('end',()=>{
const codex=args[0]==='exec';
fs.writeFileSync('received-prompt.txt',prompt);
fs.writeFileSync('project.txt','Agent chooses its own structure, with no launch manifest.');
if(codex) { console.log(JSON.stringify({type:'thread.started',thread_id:'fixture-thread'})); console.log(JSON.stringify({type:'turn.completed',usage:{input_tokens:10,cached_input_tokens:2,output_tokens:3}})); }
else { console.log(JSON.stringify({type:'system',session_id:'fixture-thread'}));console.log(JSON.stringify({type:'assistant',message:{id:'m1'}}));console.log(JSON.stringify({type:'result',usage:{input_tokens:10,cache_read_input_tokens:2,cache_creation_input_tokens:0,output_tokens:3},total_cost_usd:0.01})); }
});
`); await chmod(fake, 0o755);
  const runner = new Runner();
  runner.historyFile = path.join(await temp(), 'output-folders.json');
  await runner.start({ ...defaults, agents: ['codex','claude'].map(agent => ({ agent, path: fake, model: 'fixture' })), languages:['Python','TypeScript'], outputFolder:path.join(root,'output'), workspaceRoot:path.join(root,'workspaces'), tasks:['first-task','second-task'].map(name => ({name,prompt:`Build a solution for ${name}.`})).concat([{name:'excluded-task',enabled:false}])});
  while (runner.controller) await new Promise(resolve=>setTimeout(resolve,20));
  assert.equal(runner.batch.runs.length,8);
  assert.ok(runner.batch.runs.every(r=>r.status==='completed' && r.userPrompts===1));
  assert.deepEqual([...new Set(runner.batch.runs.map(r => r.task))].sort(), ['first-task','second-task']);
  assert.equal(new Set(runner.batch.runs.map(r => r.workspace)).size, 8);
  for (const run of runner.batch.runs) {
    const instructions = await readFile(path.join(run.artifactFolder, 'instructions.txt'), 'utf8');
    assert.ok(instructions.startsWith(`Build a solution for ${run.task}.\n\n`));
    assert.ok(instructions.includes(`Implementation language: ${run.language}.`));
    assert.equal(await readFile(path.join(run.workspace, 'received-prompt.txt'), 'utf8'), instructions);
    assert.equal(run.verification, 'agent-self-test');
    assert.equal(run.instructions, instructions);
    assert.doesNotMatch(instructions, /run\.json|acceptance|stdout|BENCHMARK_SECRET_WORD|BENCHMARK_DATA_DIR/);
    assert.equal('checks' in run, false);
    await assert.rejects(access(path.join(run.artifactFolder, 'acceptance-0.json')));
    await assert.rejects(access(path.join(run.workspace, 'run.json')));
  }
  const saved=JSON.parse(await readFile(path.join(runner.batch.folder,'results.json'),'utf8'));
  assert.equal(saved.summaries.length, 8);
  assert.ok(saved.summaries.every(s => s.attempts === 1));
  assert.equal(saved.config.tasks.length, 2);
  assert.equal(saved.analysisExcluded,true); assert.equal(saved.runs.find(r=>r.agent==='codex').usage.inputTokens,10);
  assert.ok(await readFile(path.join(runner.batch.folder,'frozen.json')));
  assert.match(await readFile(path.join(runner.batch.folder,'results.csv'),'utf8'),/fixture/);
  assert.equal(saved.runs.find(r=>r.agent==='claude').usage.modelCalls,1);
  const before=await readFile(path.join(runner.batch.folder,'results.json'),'utf8');
  // No report.md is created by this fixture, so analysis should fail without modifying results.
  const analysis=await runner.analyze({agent:'codex',path:fake,model:'fixture'});
  assert.equal(analysis.excludedFromBenchmark,true); assert.equal(analysis.status,'failed');
  assert.equal(await readFile(path.join(runner.batch.folder,'results.json'),'utf8'),before);
  const changed = new Runner();
  changed.historyFile = path.join(root, 'changed-history.json');
  await assert.rejects(changed.start({ ...runner.batch.config, reviewedInstructions: [{ task: 'first-task', language: 'Python', prompt: 'Outdated review' }] }), /instructions changed since review/);
  assert.equal(changed.controller, null);
  assert.equal(changed.batches.length, 0);
});

test('redaction removes credential shapes and known environment secrets', () => {
  process.env.BENCHMARK_TEST_API_KEY='fixture-secret-value';
  assert.equal(redact('fixture-secret-value'), '[REDACTED]');
  assert.ok(!redact('Bearer abcdefghijklmnop sk-abcdefghijklmnop').includes('abcdefghijklmnop'));
  delete process.env.BENCHMARK_TEST_API_KEY;
});

test('time limits terminate slow commands and preserve the timeout status', async () => {
  const result = await command(process.execPath, ['-e', 'setInterval(()=>{},1000)'], { timeoutMs: 50 });
  assert.equal(result.timedOut, true); assert.equal(result.cancelled, false);
});

test('frozen artifacts include logs and instructions as well as the result file', async () => {
  const folder=await temp(); await mkdir(path.join(folder,'runs','one'),{recursive:true});
  await writeFile(path.join(folder,'runs','one','agent.jsonl'),'original evidence');
  await writeFile(path.join(folder,'runs','one','instructions.txt'),'[]');
  await exportsFor({id:'fixture',folder,runs:[],config:{},environment:{}});
  const seal=JSON.parse(await readFile(path.join(folder,'frozen.json'),'utf8'));
  assert.ok(seal.artifacts['runs/one/agent.jsonl']); assert.ok(seal.artifacts['runs/one/instructions.txt']);
});

test('a second batch waits in the queue and starts when the first finishes', async () => {
  const root = await temp();
  const fake = path.join(root, 'fake-agent');
  await writeFile(fake, `#!${process.execPath}
const args=process.argv.slice(2);
if(args.includes('--help')) { console.log('--json --sandbox --skip-git-repo-check --add-dir --output-format --permission-prompts acceptEdits --settings --allowedTools'); process.exit(0); }
if(args.includes('--version')) { console.log('fixture 1.0'); process.exit(0); }
process.stdin.resume(); process.stdin.on('end',()=>{ console.log(JSON.stringify({type:'thread.started',thread_id:'t'})); console.log(JSON.stringify({type:'turn.completed',usage:{input_tokens:1,cached_input_tokens:0,output_tokens:1}})); });
`); await chmod(fake, 0o755);
  const config = name => ({ ...defaults, name, agents: [{ agent: 'codex', path: fake, model: 'fixture' }], languages: ['Python'], outputFolder: path.join(root, 'output'), workspaceRoot: path.join(root, 'workspaces'), tasks: [{ name: 'even-or-odd-cli', prompt: 'Classify an integer.' }] });
  const runner = new Runner();
  runner.historyFile = path.join(await temp(), 'output-folders.json');
  const first = await runner.start(config('first'));
  const second = await runner.start(config('second'));
  const third = await runner.start(config('third'));
  assert.equal(second.status, 'waiting');
  runner.cancel(third.key);
  assert.equal(third.status, 'cancelled');
  await assert.rejects(runner.start({ ...config('bad'), languages: [] }), /languages/);
  while (runner.controller || runner.batches.some(b => b.status === 'waiting')) await new Promise(resolve => setTimeout(resolve, 20));
  assert.deepEqual(runner.batches.map(b => b.status), ['finished', 'finished', 'cancelled']);
  assert.ok(Date.parse(second.startedAt) >= Date.parse(first.finishedAt));
  assert.ok(first.runs.concat(second.runs).every(r => r.status === 'completed'));
  assert.equal(third.runs.length, 0);
});


test('task selection validates included tasks and preserves old single-task configurations', () => {
  assert.doesNotThrow(() => validate({ ...defaults, languages: ['Dark'], darkPath: '/opt/dark', darkBuild: undefined, documentation: undefined }));
  assert.throws(() => validate({ ...defaults, languages: ['Dark'], darkPath: '' }), /executable path/);
  const config = { ...defaults, languages: ['Python'] };
  assert.throws(() => validate({ ...config, tasks: [] }), /at least one task/);
  assert.throws(() => validate({ ...config, tasks: [{ ...defaults.tasks[0], enabled: false }] }), /at least one task/);
  assert.throws(() => validate({ ...config, tasks: [{...defaults.tasks[0], enabled:true}, {...defaults.tasks[0], enabled:true}] }), /different name/);
  assert.doesNotThrow(() => validate({ ...config, tasks: [{ name: 'custom', prompt: 'Build a solution and test it.' }] }));
  const legacy = { ...config, tasks: undefined, task: { ...defaults.tasks[0], enabled: true } };
  validate(legacy);
  assert.equal(selectedTasks(legacy).length, 1);
});


test('results sort by median time and compare Dark only with matching successful runs', () => {
  const run = (language, durationMs, status = 'passed', model = 'same') => ({ task: 'timer', agent: 'codex', model, language, setupTrack: 'ready-environment', status, durationMs, usage: {} });
  const result = summaries([
    run('Dark', 80000), run('Dark', 100000), run('Python', 60000),
    run('TypeScript', 120000), run('Python', 1000, 'failed'),
    run('Python', 2000, 'passed', 'different'), run('Dark', 3000, 'failed'),
    run('Python', null, 'environment blocked')
  ]);
  assert.deepEqual(result.map(s => s.medians.durationMs), [1000, 2000, 3000, 60000, 90000, 120000, null]);
  const dark = result.find(s => s.language === 'Dark' && s.status === 'passed');
  assert.deepEqual(dark.speedComparisons, [
    { language: 'Python', ratio: 1.5 },
    { language: 'TypeScript', ratio: 0.75 }
  ]);
  assert.deepEqual(result.find(s => s.language === 'Dark' && s.status === 'failed').speedComparisons, []);
  assert.deepEqual(summaries([run('Dark', 1), run('Python', 0)])[1].speedComparisons, []);
});


test('Dark cost multiples use matching successful medians and preserve missing costs', () => {
  const run = (language, costUsd, status = 'passed') => ({ task: 'timer', agent: 'codex', model: 'same', language, setupTrack: 'ready-environment', status, durationMs: 1000, usage: { costUsd } });
  const result = summaries([run('Dark', 4), run('Dark', 8), run('Python', 3), run('TypeScript', null), run('Python', 1, 'failed')]);
  assert.deepEqual(result.find(s => s.language === 'Dark').costComparisons, [{ language: 'Python', ratio: 2 }]);
  assert.deepEqual(summaries([run('Dark', null), run('Python', 3)]).find(s => s.language === 'Dark').costComparisons, []);
  assert.deepEqual(summaries([run('Dark', 2), run('Python', 0)]).find(s => s.language === 'Dark').costComparisons, []);
});


test('saved batches return after a restart, including custom output folders', async () => {
  const root = await temp(), custom = path.join(root, 'custom'), folder = path.join(custom, 'saved-id');
  const original = { id: 'saved-id', config: { name: 'My comparison' }, startedAt: '2026-10-13T00:00:00Z', finishedAt: '2026-10-13T00:01:00Z', runs: [{ status: 'passed' }] };
  await mkdir(folder, { recursive: true });
  await writeFile(path.join(folder, 'results.json'), JSON.stringify(original));
  await writeFile(path.join(folder, 'frozen.json'), '{}');
  const saveAnalysis = async (name, agent, startedAt, report) => {
    const analysisFolder = path.join(folder, 'analysis', name);
    await mkdir(analysisFolder, { recursive: true });
    await writeFile(path.join(analysisFolder, 'analysis.json'), JSON.stringify({ status: report ? 'passed' : 'failed', agent: { agent }, startedAt }));
    if (report) await writeFile(path.join(analysisFolder, 'report.md'), report);
  };
  await saveAnalysis('codex-report', 'codex', '2026-10-13T00:02:00Z', '# Dark findings\nSaved evidence.');
  await saveAnalysis('claude-report', 'claude', '2026-10-13T00:03:00Z', '- Claude findings');
  await saveAnalysis('codex-failed', 'codex', '2026-10-13T00:04:00Z', null);
  const runner = new Runner();
  runner.historyFile = path.join(await temp(), 'output-folders.json');
  runner.historyFile = path.join(root, 'output-folders.json');
  runner.outputFolders = new Set();
  await writeFile(runner.historyFile, JSON.stringify([custom]));
  await runner.restoreHistory();
  await runner.restoreHistory();
  assert.equal(runner.batches.length, 1);
  assert.equal(runner.batches[0].name, 'My comparison');
  assert.equal(runner.batches[0].analysis.hasReport, true);
  // Each agent keeps its own report; a later failed Codex run doesn't hide the earlier one.
  assert.equal((await runner.report('saved-id', 'codex')).text, '# Dark findings\nSaved evidence.');
  assert.equal((await runner.report('saved-id', 'claude')).text, '- Claude findings');
  assert.deepEqual(Object.keys(runner.batches[0].analyses).sort(), ['claude', 'codex']);
  // A page loaded before this change asks without an agent and gets the newest finished report.
  assert.equal((await runner.report('saved-id')).text, '- Claude findings');
  await assert.rejects(runner.report('../other'), /Batch not found/);
  assert.equal(runner.batches[0].frozen, true);
  assert.equal(runner.batches[0].status, 'finished');
  assert.deepEqual(runner.batches[0].runs, original.runs);
  assert.equal(runner.controller, null);
  assert.equal(await readFile(path.join(folder, 'results.json'), 'utf8'), JSON.stringify(original));
});


test('preview contains exactly the task and the visible language guidance', () => {
  const config = { languages: ['Dark', 'Python', 'TypeScript'], tasks: [{ name: 'custom', prompt: 'Build anything. Test the program.' }], documentation: { Dark: 'My docs' } };
  const environment = { languages: { Dark: { executable: '/my/dark', version: 'v1' }, Python: {}, TypeScript: {} } };
  const preview = previewInstructions(config, environment);
  assert.equal(preview.length, 3);
  assert.equal(preview[0].prompt, 'Build anything. Test the program.\n\nImplementation language: Dark. Use this language for the actual implementation.\nSelected language executable: /my/dark\nTool version: v1\nUsing only the Dark binary above, create and run the program. Do not write any part of it in another language.\nNormal user documentation:\nMy docs\nIf permissions, missing tools or unclear requirements block you, explain the blocker plainly. Do not install dependencies.');
  assert.ok(preview.slice(1).every(p => !p.prompt.includes('/my/dark') && p.prompt.includes('Choose how to build and run')));
  for (const task of defaultTasks) assert.ok(task.prompt.includes('Test the program and explain what you tested and whether it worked.'));
});

test('completed sessions are not mixed with old checked passes in speed comparisons', () => {
  const run = (language, status) => ({ task: 't', agent: 'codex', model: 'm', language, status, setupTrack: 'ready-environment', verification: status === 'completed' ? 'agent-self-test' : undefined, durationMs: 1000, usage: {} });
  const rows = summaries([run('Dark', 'completed'), run('Python', 'passed')]);
  assert.deepEqual(rows.find(r => r.language === 'Dark').speedComparisons, []);
  const completed = summaries([run('Dark', 'completed'), run('Python', 'completed')]);
  assert.equal(completed[0].completed, 1);
  assert.equal(completed.find(r => r.language === 'Dark').speedComparisons[0].ratio, 1);
});

test('deleting a batch removes all saved attempts and projects and survives restart', async () => {
  const root = await temp(), outputFolder = path.join(root, 'results'), folder = path.join(outputFolder, 'batch-one'), workspaceRoot = path.join(root, 'projects');
  for (const id of ['one', 'two']) {
    await mkdir(path.join(workspaceRoot, 'batch-one', id), { recursive: true });
    await writeFile(path.join(workspaceRoot, 'batch-one', id, 'main.py'), 'print(1)');
    await mkdir(path.join(folder, 'runs', id), { recursive: true });
    await writeFile(path.join(folder, 'runs', id, 'agent-0.jsonl'), 'log');
  }
  await mkdir(path.join(folder, 'analysis', 'old'), { recursive: true });
  await writeFile(path.join(folder, 'analysis', 'old', 'report.md'), 'Old report');
  const batch = { key: 'key', id: 'batch-one', folder, config: { name: 'Comparison', outputFolder, workspaceRoot }, runs: [], frozen: true, status: 'finished', events: [], analysis: { status: 'passed' } };
  const other = { key: 'other', status: 'cancelled' };
  await exportsFor(batch);
  const runner = new Runner(); runner.batches = [batch, other]; runner.batch = batch;
  await runner.deleteBatch('key');
  assert.deepEqual(runner.batches, [other]);
  assert.equal(runner.batch, null);
  await assert.rejects(access(path.join(workspaceRoot, 'batch-one')));
  await assert.rejects(access(folder));
  const restored = new Runner(); restored.historyFile = path.join(root, 'missing-history.json'); restored.outputFolders = new Set([outputFolder]);
  await restored.restoreHistory();
  assert.equal(restored.batches.length, 0);
  await runner.deleteBatch('other');
  assert.equal(runner.batches.length, 0);
});

test('batch deletion rejects active work, unknown batches and mismatched output paths', async () => {
  const runner = new Runner();
  const batch = { key: 'key', id: 'batch', config: { outputFolder: '/tmp/results', workspaceRoot: '/tmp/projects' }, folder: '/tmp/unrelated', status: 'running' };
  runner.batches = [batch];
  await assert.rejects(runner.deleteBatch('key'), /finish before deleting/);
  batch.status = 'finished'; batch.analysis = { status: 'running' };
  await assert.rejects(runner.deleteBatch('key'), /finish before deleting/);
  batch.analysis = null;
  await assert.rejects(runner.deleteBatch('key'), /output path does not match/);
  await assert.rejects(runner.deleteBatch('missing'), /Batch not found/);
});

test('an expired login stops the batch before any attempt and the CLI message is shown', async () => {
  const root = await temp();
  const fake = path.join(root, 'fake-agent');
  await writeFile(fake, `#!${process.execPath}
const args=process.argv.slice(2);
if(args.includes('--help')) { console.log('--json --sandbox --skip-git-repo-check --add-dir --output-format --permission-prompts acceptEdits --settings --allowedTools'); process.exit(0); }
if(args.includes('--version')) { console.log('fixture 1.0'); process.exit(0); }
process.stdin.resume(); process.stdin.on('end',()=>{ console.log(JSON.stringify({type:'result',is_error:true,result:'Failed to authenticate: OAuth session expired and could not be refreshed'})); process.exit(1); });
`); await chmod(fake, 0o755);
  const runner = new Runner();
  runner.historyFile = path.join(await temp(), 'output-folders.json');
  const config = { ...defaults, agents: [{ agent: 'claude', path: fake, model: 'fixture' }], languages: ['Python'], outputFolder: path.join(root, 'output'), workspaceRoot: path.join(root, 'workspaces'), tasks: [{ name: 'even-or-odd-cli', prompt: 'Classify an integer.' }] };
  await assert.rejects(runner.start(config), /Claude could not start, so the batch was not run: Failed to authenticate: OAuth session expired/);
  assert.equal(runner.batches.length, 0);
  assert.equal(runner.controller, null);
  await assert.rejects(access(path.join(root, 'output')));
});

test('network stays off unless a task lists sites, and then only those sites are allowed', async () => {
  const { launchArgs } = await import('../src/adapters.js');
  const settings = args => JSON.parse(args[args.indexOf('--settings') + 1]).sandbox;
  assert.deepEqual(settings(launchArgs('claude', '', [])).network.allowedDomains, []);
  assert.equal(settings(launchArgs('claude', '', [])).strictAllowlist, true);
  assert.deepEqual(settings(launchArgs('claude', '', [], ['api.github.com'])).network.allowedDomains, ['api.github.com']);
  assert.ok(!launchArgs('codex', '', []).some(arg => arg.includes('network')));
  const codex = launchArgs('codex', '', [], ['api.github.com', 'github.com']);
  assert.ok(codex.includes('sandbox_workspace_write.network_access=true') && codex.includes('features.network_proxy.enabled=true'));
  assert.ok(codex.includes('features.network_proxy.domains={ "api.github.com" = "allow", "github.com" = "allow" }'));
  assert.equal(codex.at(-1), '-');
  const { validate, defaults } = await import('../src/config.js');
  const config = sites => ({ ...defaults, darkPath: '/opt/dark', tasks: [{ name: 't', prompt: 'p', networkSites: sites }] });
  assert.doesNotThrow(() => validate(config(['api.github.com', '*.npmjs.org'])));
  assert.throws(() => validate(config(['https://api.github.com'])), /host names/);
  assert.throws(() => validate(config(['a" = "allow" }'])), /host names/);
  const { selectedTasks } = await import('../src/config.js');
  // An outdated page omits the field; the GitHub task still gets its sites, an explicit empty list stays offline.
  assert.deepEqual(selectedTasks({ tasks: [{ templateId: 'github-stats', name: 'github-stats', prompt: 'p' }] })[0].networkSites, ['api.github.com', 'github.com']);
  assert.deepEqual(selectedTasks({ tasks: [{ name: 'github-stats', prompt: 'p', networkSites: [] }] })[0].networkSites, []);
});
