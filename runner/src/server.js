import http from 'node:http';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomBytes, randomUUID } from 'node:crypto';
import { previewInstructions, analysisPrompt } from './instructions.js';
import { launchArgs } from './adapters.js';
import { darkRuntimePaths } from './dark-runtime.js';
import { modelChoices } from './model-choices.js';
import { projectFiles } from './project-files.js';
import { tryProgram, savedTries, suggestedCommand } from './try-program.js';
import { Runner } from './runner.js';
import { defaults, preflight, selectedTasks } from './config.js';
import { summaries, redact } from './storage.js';

const runner = new Runner();
await runner.restoreHistory();
const publicFolder = fileURLToPath(new URL('../public/', import.meta.url));
const token = randomBytes(32).toString('hex');
// Let an open dashboard detect a restart and reload updated page code.
const version = randomUUID();
const port = Number(process.env.PORT || 4317);
let trying = false;
const server = http.createServer(async (req, res) => {
  const json = (value, status = 200) => { res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }); res.end(redact(JSON.stringify(value))); };
  try {
    // Check the host and browser origin as well as listening only on localhost.
    if (req.headers.host !== `127.0.0.1:${port}` && req.headers.host !== `localhost:${port}`) return json({ error: 'Invalid host' }, 403);
    if (req.headers.origin && ![`http://127.0.0.1:${port}`, `http://localhost:${port}`].includes(req.headers.origin)) return json({ error: 'Invalid origin' }, 403);
    if (req.url === '/api/defaults' && req.method === 'GET') return json({ config: defaults, token, analysisPrompt, modelChoices: await modelChoices(runner.batches) });
    if (req.url === '/api/state' && req.method === 'GET') {
      // Keep the full environment checks in environment.json, outside this dashboard response.
      const batches = runner.batches.map(({ environment, ...b }) => ({ ...b, summaries: summaries(b.runs) }));
      return json({ batches, version, active: runner.controller ? runner.batch?.key ?? null : null, busy: !!runner.controller });
    }
    if (req.method === 'POST') {
      if (req.headers['x-benchmark-token'] !== token) return json({ error: 'Invalid request token' }, 403);
      let body = ''; for await (const chunk of req) { body += chunk; if (body.length > 2_000_000) throw Error('Request too large.'); }
      const value = JSON.parse(body || '{}');
      if (req.url === '/api/preview') {
        const config = { ...value, tasks: selectedTasks(value) };
        const environment = await preflight(config);
        const runtimeId = config.languages.includes('Dark') ? randomUUID() : null;
        const runtime = runtimeId ? darkRuntimePaths(config, runtimeId) : null;
        config.runtimeId = runtimeId;
        const prompts = previewInstructions(config, environment);
        return json({ runtimeId, runtime, prompts, cliSettings: prompts.map(p => {
          const agent = config.agents.find(a => a.agent === p.agent), task = config.tasks.find(t => t.name === p.task);
          return { task: p.task, language: p.language, repetition: p.repetition, agent: agent.agent, executable: agent.path,
            args: launchArgs(agent.agent, agent.model, p.runtime?.writableDirs || [], task.networkSites || []), env: p.runtime?.env || {} };
        }), analysisPrompt });
      }
      if (req.url === '/api/preflight') return json(await preflight(value));
      if (req.url === '/api/start') return json(await runner.start(value));
      if (req.url === '/api/delete-batch') { await runner.deleteBatch(value.batch); return json({ ok: true }); }
      if (req.url === '/api/cancel') { runner.cancel(value.batch); return json({ ok: true }); }
      if (req.url === '/api/note') { runner.note(value.text); return json({ ok: true }); }
      if (req.url === '/api/code') {
        const batch = runner.find(value.batch), run = batch?.runs.find(r => r.id === value.run);
        if (!run) throw Error('Attempt not found.');
        if (['queued', 'running'].includes(run.status)) throw Error('Code is available when the attempt finishes.');
        const project = await projectFiles(run.workspace);
        return json({ ...project, tries: await savedTries(batch, run), suggestedCommand: suggestedCommand(run.language, project.files.map(f => f.name)) });
      }
      if (req.url === '/api/try') {
        const batch = runner.find(value.batch), run = batch?.runs.find(r => r.id === value.run);
        if (!run) throw Error('Attempt not found.');
        if (['queued', 'running'].includes(run.status)) throw Error('The program can be run when the attempt finishes.');
        // Keep manual runs from competing with timed attempts or analysis.
        if (runner.controller) throw Error('Wait for the current benchmark or analysis to finish before running a program.');
        if (trying) throw Error('Another program is still running.');
        trying = true;
        try { return json(await tryProgram(batch, run, { commandLine: value.command, input: value.input })); } finally { trying = false; }
      }
      if (req.url === '/api/report') return json(await runner.report(value.batch, value.agent));
      if (req.url === '/api/analyze') { const { batch, ...agent } = value; return json(await runner.analyze(agent, batch)); }
    }
    const file = { '/': ['index.html', 'text/html'], '/app.js': ['app.js', 'text/javascript'], '/style.css': ['style.css', 'text/css'] }[req.url];
    if (!file || req.method !== 'GET') return json({ error: 'Not found' }, 404);
    res.writeHead(200, { 'Content-Type': file[1], 'Cache-Control': 'no-cache', 'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'" });
    res.end(await readFile(path.join(publicFolder, file[0])));
  } catch (error) { json({ error: error.message }, 400); }
});
server.on('error', error => {
  console.error(`Cannot start the local dashboard: ${error.message}. Run npm start in a host terminal that allows local listening sockets.`);
  process.exitCode = 1;
});
server.listen(port, '127.0.0.1', () => console.log(`Benchmark dashboard: http://127.0.0.1:${port}`));
const stop = () => { if (runner.controller) runner.cancel(); server.close(); };
process.on('SIGINT', stop); process.on('SIGTERM', stop);
