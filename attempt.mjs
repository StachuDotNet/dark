// usage: node attempt.mjs <label> <task> <n>
// One attempt: fresh store, fresh workspace, isolated claude, then one jsonl row in results/<label>.jsonl.
// Uses the W467 runner's own modules, so prompts, sandbox and store setup match the runner's.
import { readFileSync, existsSync } from 'node:fs';
import { mkdir, writeFile, appendFile } from 'node:fs/promises';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import path from 'node:path';
import { prepareDarkRuntime, darkAttemptId } from './runner/src/dark-runtime.js';
import { preflight } from './runner/src/config.js';
import { instructionsFor } from './runner/src/instructions.js';
import { runAgent } from './runner/src/adapters.js';
import { metrics, tokens } from './metrics.mjs';

const B = path.dirname(new URL(import.meta.url).pathname);
const [label, taskName, n] = process.argv.slice(2);
const model = process.env.MODEL || 'claude-opus-5-5';
const language = process.env.BENCH_LANG || 'Dark'; // Python: the same task and runner, for a floor to compare with
const WS = '/home/stachu/code/bench-loop-ws';

const sets = ["orig", "train", "held-out", "held-out-2", "probe"];
const set = sets.find(s => existsSync(`${B}/tasks/${s}/${taskName}.md`));
if (!set) throw Error(`no task ${taskName}`);
const md = readFileSync(`${B}/tasks/${set}/${taskName}.md`, 'utf8');
const prompt = md.split('<!-- Everything below this line')[0].trim();
const siteLine = md.match(/\*\*Network sites:\*\*(.*)/)?.[1] || '';
const networkSites = taskName === 'github-stats' ? ['api.github.com', 'github.com'] : [...siteLine.matchAll(/`([^`]+)`/g)].map(m => m[1]);
const seedName = md.match(/\*\*Seed:\*\*\s*`seeds\/([\w-]+)`/)?.[1];
const seed = seedName ? `${B}/tasks/seeds/${seedName}` : null;
const minutes = set === 'orig' ? 20 : 30;

const task = { templateId: taskName, name: taskName, enabled: true, prompt, networkSites };
const runtimeId = `${label}-${taskName}-${n}-${randomBytes(3).toString('hex')}`.replace(/[^a-zA-Z0-9-]/g, '-');
// BENCH_AGENT: claude (default); dark-agent (store tools and checks, one trajectory); dark-agent-attempts (the same, up to 3 attempts on fresh branches); dark-agent-generic (files and bash only)
const AGENT = process.env.BENCH_AGENT || 'claude';
const agentEntry = AGENT.startsWith('dark-agent') ? { agent: AGENT, path: `${B}/bins/${label}/dark`, model } : { agent: 'claude', path: `${B}/claude-iso`, model };
const config = { agents: [agentEntry], languages: [language],
  darkPath: `${B}/bins/${label}/dark`, darkBuild: 'release', repetitions: 1, minutes,
  outputFolder: `${B}/results/${label}`, workspaceRoot: WS, tasks: [task], documentation: { Dark: '' },
  setupTrack: 'ready-environment', runtimeId };
const run = { task: taskName, agent: AGENT, model, repetition: +n };
const out = `${B}/results/${label}/${taskName}-${n}`;
await mkdir(out, { recursive: true });
const workspace = path.join(WS, label, `${taskName}-${n}-${runtimeId.slice(-6)}`);
await mkdir(workspace, { recursive: true });

const row = { label, task: taskName, n: +n, model, status: null, s: null, setupS: null, ws: workspace, out };
const setup0 = performance.now();
let runtime, environment;
try {
  if (language !== 'Dark') {
    runtime = { env: {}, writableDirs: [] };
    environment = await preflight(config, undefined, runtime);
    if (seed) execFileSync('/bin/sh', ['-c', `cp -r "${seed}/python/." . && { [ ! -d "${seed}/fixtures" ] || cp -r "${seed}/fixtures" .; }`], { cwd: workspace, stdio: ['ignore', 'pipe', 'pipe'] });
  } else {
  runtime = await prepareDarkRuntime(config, runtimeId, darkAttemptId(run));
  // the wrapper makes the attempt folder Dark's HOME, and .NET keeps its shared-memory locks in HOME/.dotnet
  const homeDotnet = path.join(runtime.root, '.dotnet');
  await mkdir(homeDotnet, { recursive: true });
  runtime.writableDirs = [...runtime.writableDirs, homeDotnet];
  if (AGENT.startsWith('dark-agent')) {
    // the agent reads its key beside the store: the attempt's rundir, else HOME/.darklang
    const secrets = path.join(runtime.env.DARK_CONFIG_RUNDIR || path.join(runtime.root, '.darklang'), 'secrets');
    await mkdir(secrets, { recursive: true });
    execFileSync('/bin/cp', [process.env.DARK_AGENT_KEY_FILE, path.join(secrets, 'anthropic.key')]);
  }
  environment = await preflight(config, undefined, runtime);
  if (seed) execFileSync('/bin/sh', ['-c', `cp -r "${seed}/dark/." . && { [ ! -d "${seed}/fixtures" ] || cp -r "${seed}/fixtures" .; } && rm -f setup.sh && DARK="${runtime.binaryPath}" sh "${seed}/dark/setup.sh"`],
    { cwd: workspace, env: { ...process.env, ...runtime.env }, stdio: ['ignore', 'pipe', 'pipe'] });
  }
} catch (e) {
  row.status = 'setup failed'; row.error = String(e.message).slice(0, 500);
  row.setupS = Math.round((performance.now() - setup0) / 1000);
  await appendFile(`${B}/results/${label}.jsonl`, JSON.stringify(row) + '\n');
  process.exit(1);
}
row.setupS = Math.round((performance.now() - setup0) / 1000);
const instructions = instructionsFor(task, language, config, { ...environment, languages: { [language]: environment.languages[language] } }, run);
await writeFile(`${out}/prompt.txt`, instructions);

const started = performance.now(), deadline = Date.now() + minutes * 60000;
const seg = await runAgent(config.agents[0], instructions, { cwd: workspace, writableDirs: runtime.writableDirs, networkSites, env: { ...runtime.env, NO_PROXY: '127.0.0.1,localhost', no_proxy: '127.0.0.1,localhost' }, timeoutMs: deadline - Date.now() });
row.s = Math.round((performance.now() - started) / 1000);
await writeFile(`${out}/agent-0.jsonl`, seg.stdout);
await writeFile(`${out}/stderr-0.txt`, seg.stderr);
row.status = seg.timedOut || Date.now() >= deadline ? 'limit reached'
  : (seg.error || seg.code !== 0 || seg.agentError) ? 'environment blocked'
  : seg.denials.length ? 'completed (denials)' : 'completed';
row.version = environment.languages[language].version; row.language = language;
const u = seg.usage;
Object.assign(row, { calls: u.modelCalls, cost: +(u.costUsd || 0).toFixed(3), sandboxDenials: seg.denials.length }, tokens(seg.stdout), metrics(seg.stdout));
await writeFile(`${out}/run.json`, JSON.stringify({ ...row, runtime: { root: runtime.root }, args: seg.args }, null, 2));
await appendFile(`${B}/results/${label}.jsonl`, JSON.stringify(row) + '\n');
console.log(`${new Date().toTimeString().slice(0, 8)} ${label} ${taskName}#${n} ${row.status} ${row.s}s ${row.calls} calls $${row.cost}`);
