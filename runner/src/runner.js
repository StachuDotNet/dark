import { mkdir, readFile, readdir, rm } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID, randomInt, createHash } from 'node:crypto';
import { performance } from 'node:perf_hooks';
import { darkRuntimePaths, darkAttemptId, prepareDarkRuntime } from './dark-runtime.js';
import { defaults, preflight, validate, selectedTasks } from './config.js';
import { modelUsed } from './model-choices.js';
import { blankUsage, runAgent, checkLogin, eventsFrom, errorText } from './adapters.js';
import { createActivityFormatter, savedActivity } from './activity.js';
import { instructionsFor, previewInstructions, analysisPrompt } from './instructions.js';
import { save, redact, combineUsage, exportsFor } from './storage.js';

// Older blocked attempts saved only a generic message; read the CLI's own reason from its log.
async function savedErrorText(run) {
  try {
    const events = eventsFrom(await readFile(path.join(run.artifactFolder, 'agent-0.jsonl'), 'utf8'));
    return errorText(events, events.findLast(e => e.type === 'result'));
  } catch { return null; }
}

export class Runner {
  // Every batch requested this session, oldest first. Waiting batches run one at a time, in order,
  // so attempts never compete for the machine.
  batches = [];
  batch = null;
  controller = null;
  historyFile = path.resolve('.cache/output-folders.json');
  outputFolders = new Set([defaults.outputFolder]);
  async restoreHistory() {
    try {
      for (const folder of JSON.parse(await readFile(this.historyFile, 'utf8'))) this.outputFolders.add(folder);
    } catch (error) { if (error.code !== 'ENOENT') console.warn('Could not read saved output folders:', error.message); }
    for (const root of this.outputFolders) {
      let entries;
      try { entries = await readdir(root, { withFileTypes: true }); }
      catch (error) { if (error.code !== 'ENOENT') console.warn('Could not read benchmark history:', error.message); continue; }
      for (const entry of entries.filter(e => e.isDirectory())) {
        const folder = path.join(root, entry.name);
        try {
          const saved = JSON.parse(await readFile(path.join(folder, 'results.json'), 'utf8'));
          if (!saved.id || !saved.config || !Array.isArray(saved.runs)) throw Error('Invalid saved batch.');
          if (this.batches.some(b => b.id === saved.id)) continue;
          let frozen = false;
          try { await readFile(path.join(folder, 'frozen.json')); frozen = true; } catch {}
          const analyses = await this.savedAnalyses(folder);
          const analysis = Object.values(analyses).sort((a, b) => String(b.startedAt).localeCompare(String(a.startedAt)))[0] || null;
          // Fill in the model for older results without changing the saved files.
          for (const run of saved.runs) if (!run.modelUsed) { const used = await modelUsed(run); if (used) run.modelUsed = used; }
          for (const run of saved.runs) if (run.status === 'environment blocked' && /^The CLI failed or reported/.test(run.error || '')) {
            const text = await savedErrorText(run);
            if (text) run.error = `The CLI reported: ${redact(text)}`;
          }
          this.batches.push({ ...saved, analysis, analyses, key: saved.id, name: saved.config.name || 'Saved batch', folder, frozen,
            status: saved.runs.some(r => r.status === 'cancelled') ? 'cancelled' : 'finished',
            progress: 'Saved results restored', events: await savedActivity(folder, saved.runs).catch(error => [{ kind:'error', title:'Saved activity unavailable', text:redact(error.message), details:'', at:null }]) });
        } catch (error) { if (error.code !== 'ENOENT') console.warn(`Could not restore ${folder}:`, error.message); }
      }
    }
    this.batches.sort((a, b) => String(a.startedAt).localeCompare(String(b.startedAt)));
  }
  // The newest finished report from each analysis agent, so one agent's report never hides the other's.
  async savedAnalyses(batchFolder) {
    const root = path.join(batchFolder, 'analysis');
    let entries;
    try { entries = await readdir(root, { withFileTypes: true }); } catch (error) { if (error.code === 'ENOENT') return {}; throw error; }
    const reports = [];
    for (const entry of entries.filter(e => e.isDirectory())) {
      const folder = path.join(root, entry.name);
      try {
        const record = JSON.parse(await readFile(path.join(folder, 'analysis.json'), 'utf8'));
        let hasReport = false;
        try { await readFile(path.join(folder, 'report.md')); hasReport = true; } catch {}
        reports.push({ ...record, folder, hasReport });
      } catch (error) { if (error.code !== 'ENOENT') console.warn('Could not read saved analysis:', error.message); }
    }
    const latest = {};
    for (const report of reports.sort((a, b) => String(b.startedAt).localeCompare(String(a.startedAt)))) {
      const agent = report.agent?.agent || 'unknown';
      // A newer failed run doesn't hide an older report that did finish.
      if (!latest[agent] || (!latest[agent].hasReport && report.hasReport)) latest[agent] = report;
    }
    return latest;
  }
  async report(key, agent) {
    const batch = this.find(key);
    if (!batch) throw Error('Batch not found.');
    const analyses = await this.savedAnalyses(batch.folder);
    // Pages loaded before per-agent reports existed don't send an agent; show the newest report.
    const analysis = agent ? analyses[agent] : Object.values(analyses).filter(a => a.hasReport).sort((a, b) => String(b.startedAt).localeCompare(String(a.startedAt)))[0];
    if (!analysis?.hasReport) throw Error(`There is no saved ${agent ? `${agent} ` : ''}report for this batch yet.`);
    return { text: await readFile(path.join(analysis.folder, 'report.md'), 'utf8'), analysis };
  }
  async start(config) {
    validate(config);
    this.outputFolders.add(config.outputFolder);
    await save(this.historyFile, [...this.outputFolders]);
    config = structuredClone(config);
    config.tasks = selectedTasks(config);
    delete config.task;
    delete config.repairs;
    const entry = { key: randomUUID().slice(0, 8), name: String(config.name || 'Untitled batch').slice(0, 80), config: structuredClone(config), status: 'waiting', progress: 'Waiting for the current session to finish', queuedAt: new Date().toISOString(), runs: [], events: [] };
    this.batches.push(entry);
    if (this.controller) return entry;
    try { await this.launch(entry); } catch (error) { this.batches.splice(this.batches.indexOf(entry), 1); this.next(); throw error; }
    return entry;
  }
  async launch(entry) {
    const config = entry.config;
    this.controller = new AbortController();
    this.batch = entry;
    entry.status = 'preparing'; entry.progress = 'Checking the environment';
    let runtime;
    try {
      if (config.languages.includes('Dark')) {
        config.runtimeId ||= randomUUID();
        runtime = await prepareDarkRuntime(config, config.runtimeId);
      }
      const environment = await preflight(config, this.controller.signal, runtime);
      if (this.controller.signal.aborted) throw Error('Start cancelled during preflight.');
      entry.progress = 'Checking that each agent can sign in';
      environment.loginChecks = [];
      for (const agent of config.agents) {
        const check = await checkLogin(agent, this.controller.signal, runtime?.env);
        environment.loginChecks.push(check);
        if (this.controller.signal.aborted) throw Error('Start cancelled during preflight.');
        if (!check.ok) throw Error(`${agent.agent === 'claude' ? 'Claude' : 'Codex'} could not start, so the batch was not run: ${redact(check.message)} Log in to that CLI again, then retry.`);
      }
      if (config.reviewedInstructions && JSON.stringify(config.reviewedInstructions) !== JSON.stringify(previewInstructions(config, environment))) throw Error('The instructions changed since review. Review the configuration again.');
      const id = `${new Date().toISOString().replaceAll(':', '-')}-${randomUUID().slice(0, 8)}`;
      const folder = path.join(config.outputFolder, id);
      const runs = [];
      for (const task of config.tasks) for (const agent of config.agents) for (const language of config.languages) for (let repetition = 1; repetition <= config.repetitions; repetition++) {
        runs.push({ id: randomUUID(), task: task.name, agent: agent.agent, model: agent.model || '(CLI default)', language, repetition, setupTrack: config.setupTrack, status: 'queued', usage: blankUsage(), userPrompts: 0, interventions: [], segments: [] });
      }
      // Save the actual shuffled order. Every attempt is still run sequentially.
      // docs A/B: keep the configured (interleaved) order.
      Object.assign(entry, { id, folder, environment, runs, status: 'running', startedAt: new Date().toISOString(), progress: 'Starting' });
      await save(path.join(folder, 'config.json'), config);
      await save(path.join(folder, 'environment.json'), environment);
      this.execute().catch(async error => {
        entry.error = redact(error.message); entry.status = 'failed';
        for (const r of entry.runs) if (['queued', 'running'].includes(r.status)) { r.status = 'environment blocked'; r.error = entry.error; }
        await this.freeze();
      });
      return entry;
    } catch (error) {
      if (runtime) await rm(runtime.root, { recursive: true, force: true });
      this.controller = null; throw error;
    }
  }
  // Start the next waiting batch, if any. Preflight failures are recorded on the batch and skipped.
  next() {
    if (this.controller) return;
    const entry = this.batches.find(b => b.status === 'waiting');
    if (!entry) return;
    this.launch(entry).catch(error => {
      entry.status = /cancelled/.test(error.message) ? 'cancelled' : 'failed';
      entry.error = redact(error.message); entry.progress = 'Did not start';
      this.next();
    });
  }
  find(key) { return key ? this.batches.find(b => b.key === key) : this.batch; }
  async deleteBatch(key) {
    const batch = this.find(key);
    if (!batch) throw Error('Batch not found.');
    if (batch.deleting || ['waiting', 'running', 'preparing'].includes(batch.status) || (this.controller && this.batch === batch) || batch.analysis?.status === 'running') throw Error('Cancel the batch or wait for it and its analysis to finish before deleting it.');
    if (batch.folder) {
      if (!batch.id || ['.', '..'].includes(batch.id) || !/^[a-zA-Z0-9_.-]+$/.test(batch.id)) throw Error('Invalid batch folder name.');
      const expectedFolder = path.resolve(batch.config.outputFolder, batch.id);
      if (path.resolve(batch.folder) !== expectedFolder) throw Error('The saved output path does not match this batch.');
      batch.deleting = true;
      try {
        await rm(path.join(batch.config.workspaceRoot, batch.id), { recursive: true, force: true });
        if (batch.config.runtimeId) await rm(darkRuntimePaths(batch.config, batch.config.runtimeId).root, { recursive: true, force: true });
        await rm(expectedFolder, { recursive: true, force: true });
      } finally { batch.deleting = false; }
    }
    this.batches = this.batches.filter(b => b !== batch);
    if (this.batch === batch) this.batch = null;
  }
  cancel(key) {
    const entry = this.find(key);
    if (entry?.status === 'waiting') { entry.status = 'cancelled'; entry.progress = 'Removed from the queue before starting'; return; }
    if (!this.controller) throw Error('Nothing is running.');
    if (entry && entry !== this.batch) throw Error('That batch is not running.');
    this.controller.abort(); if (this.batch) this.batch.progress = 'Stopping processes and saving results';
  }
  note(text) {
    if (!this.controller || !text?.trim()) throw Error('A response needs an active session and some text.');
    const run = this.batch.runs.find(r => r.status === 'running');
    if (!run) throw Error('There is no benchmark attempt running.');
    // Save the note without sending it to the agent.
    run.interventions.push({ at: new Date().toISOString(), text: redact(text), kind: 'human note', deliveredToAgent: false });
  }
  async execute() {
    const batch = this.batch, signal = this.controller.signal;
    for (const run of batch.runs) {
      if (signal.aborted) { run.status = 'cancelled'; continue; }
      await this.attempt(run, signal);
      await save(path.join(batch.folder, 'progress.json'), { runs: batch.runs, status: batch.status });
    }
    batch.status = signal.aborted ? 'cancelled' : 'finished';
    await this.freeze();
  }
  async freeze() {
    this.batch.finishedAt = new Date().toISOString();
    await exportsFor(this.batch);
    this.batch.frozen = true;
    this.batch.progress = 'Results saved and frozen';
    this.controller = null;
    this.next();
  }
  async attempt(run, signal) {
    const batch = this.batch, config = batch.config;
    const task = config.tasks.find(task => task.name === run.task);
    const folder = path.join(batch.folder, 'runs', run.id);
    const workspace = path.join(config.workspaceRoot, batch.id, run.id);
    await mkdir(workspace, { recursive: true });
    const agent = config.agents.find(a => a.agent === run.agent && (a.model || '(CLI default)') === run.model);
    run.workspace = workspace; run.artifactFolder = folder;
    let runtime = null;
    if (run.language === 'Dark') {
      const setupStarted = performance.now();
      try {
        batch.progress = `Preparing fresh Dark state for ${run.task} / ${run.agent} / repetition ${run.repetition}`;
        runtime = await prepareDarkRuntime({ ...config, darkPath: task.darkPath || batch.environment.runtime.binaryPath }, config.runtimeId, darkAttemptId(run));
        run.runtime = runtime;
        const environment = await preflight(config, signal, runtime);
        await save(path.join(folder, 'environment.json'), environment);
      } catch (error) {
        run.status = signal.aborted ? 'cancelled' : 'environment blocked';
        run.error = redact(`Fresh Dark setup failed: ${error.message}`);
        run.finishedAt = new Date().toISOString();
        run.durationMs = null;
        run.setupDurationMs = Math.round(performance.now() - setupStarted);
        await save(path.join(folder, 'run.json'), run);
        return;
      }
      run.setupDurationMs = Math.round(performance.now() - setupStarted);
    }
    // docs A/B: put a seed in place before timing starts (seeds/README.md).
    if (task.seed && run.language === 'Dark') {
      const { execFileSync } = await import('node:child_process');
      execFileSync('/bin/sh', ['-c', `cp -r "${task.seed}/dark/." . && { [ ! -d "${task.seed}/fixtures" ] || cp -r "${task.seed}/fixtures" .; } && rm -f setup.sh && DARK="${runtime.binaryPath}" sh "${task.seed}/dark/setup.sh"`], { cwd: workspace, env: { ...process.env, ...runtime.env }, stdio: ['ignore', 'pipe', 'pipe'] });
    }
    const prompt = instructionsFor(task, run.language, config, batch.environment, run);
    await save(path.join(folder, 'instructions.txt'), prompt);
    run.workspace = workspace; run.artifactFolder = folder; run.status = 'running';
    run.startedAt = new Date().toISOString();
    const started = performance.now(), deadline = Date.now() + config.minutes * 60000;
    run.verification = 'agent-self-test';
    run.instructions = prompt;
    try {
      batch.progress = `${run.task} / ${run.agent} / ${run.language} / repetition ${run.repetition}`;
      run.userPrompts = 1;
      await save(path.join(folder, 'prompt-0.txt'), prompt);
      const formatActivity = createActivityFormatter();
      const appendActivity = rows => {
        for (const row of rows) batch.events.push({ ...row, text: redact(row.text), details: redact(row.details), runId: run.id, at: new Date().toISOString() });
        if (batch.events.length > 150) batch.events.splice(0, batch.events.length - 150);
      };
      const segment = await runAgent(agent, prompt, { cwd: workspace, writableDirs: runtime?.writableDirs || [], networkSites: task.networkSites || [], env: runtime?.env, timeoutMs: deadline - Date.now(), signal,
        onLine: line => appendActivity(formatActivity(line)),
        onStderrLine: line => appendActivity([{ kind: 'error', title: 'CLI diagnostic', text: line, details: '' }]) });
      await save(path.join(folder, 'agent-0.jsonl'), segment.stdout);
      await save(path.join(folder, 'stderr-0.txt'), segment.stderr);
      run.segments.push({ reportedModels: [...new Set(segment.events.flatMap(e => [e.model, e.message?.model].filter(Boolean)))], sessionId: segment.sessionId, args: segment.args, usage: segment.usage, exitCode: segment.code, denials: segment.denials, error: segment.error });
      run.usage = combineUsage([segment.usage]);
      run.modelUsed = await modelUsed(run);
      if (segment.cancelled || signal.aborted) run.status = 'cancelled';
      else if (segment.timedOut || Date.now() >= deadline) run.status = 'limit reached';
      else if (segment.error || segment.code !== 0 || segment.agentError || segment.denials.length) {
        run.status = 'environment blocked';
        run.error = segment.error || (segment.errorText ? `The CLI reported: ${redact(segment.errorText)}` : 'The CLI failed or reported a permission/environment blocker. See its logs.');
      } else run.status = 'completed';
      if (run.status !== 'completed') appendActivity([{ kind: 'error', title: `Attempt ${run.status}`, text: run.error || (run.status === 'limit reached' ? 'The time limit was reached.' : 'The attempt was cancelled.'), details: '' }]);
    } catch (error) { run.status = signal.aborted ? 'cancelled' : 'environment blocked'; run.error = redact(error.message); }
    run.finishedAt = new Date().toISOString(); run.durationMs = Math.round(performance.now() - started);
    await save(path.join(folder, 'run.json'), run);
    // Keep the original project folder instead of copying files that may contain symlinks.
    await save(path.join(folder, 'final-project.json'), { path: workspace, note: 'Final project remains in its original isolated workspace.' });
  }
  async analyze(agent, key) {
    const batch = this.find(key);
    if (this.controller || !batch?.frozen || batch.deleting) throw Error('Analysis needs a completely finished and frozen batch.');
    if (!['codex', 'claude'].includes(agent.agent) || !agent.path) throw Error('Choose an analysis agent.');
    const results = await readFile(path.join(batch.folder, 'results.json'));
    const seal = JSON.parse(await readFile(path.join(batch.folder, 'frozen.json'), 'utf8'));
    if (createHash('sha256').update(results).digest('hex') !== seal.sha256) throw Error('The frozen results have changed.');
    for (const [relative, hash] of Object.entries(seal.artifacts ?? {})) {
      const bytes = await readFile(path.join(batch.folder, relative));
      if (createHash('sha256').update(bytes).digest('hex') !== hash) throw Error(`Frozen evidence changed: ${relative}`);
    }
    const folder = path.join(batch.folder, 'analysis', randomUUID());
    const cwd = path.join(batch.config.workspaceRoot, 'analysis', randomUUID());
    await mkdir(cwd, { recursive: true });
    const dark = batch.runs.filter(r => r.language === 'Dark');
    let evidence = results.toString();
    for (const run of dark) {
      evidence += `\nTASK ${run.task} / ${run.agent} / repetition ${run.repetition} / total duration ${run.durationMs ?? 'unknown'} ms / status ${run.status}\n`;
      for (const segment of run.segments) {
        evidence += await readFile(path.join(run.artifactFolder, `agent-${segment.repair ?? 0}.jsonl`), 'utf8');
        try { evidence += '\nCLI ERRORS:\n' + await readFile(path.join(run.artifactFolder, `stderr-${segment.repair ?? 0}.txt`), 'utf8'); } catch (error) { if (error.code !== 'ENOENT') throw error; }
        try { evidence += await readFile(path.join(run.artifactFolder, `acceptance-${segment.repair}.json`), 'utf8'); } catch {}
      }
    }
    await save(path.join(cwd, 'evidence.txt'), evidence);
    const prompt = analysisPrompt;
    this.controller = new AbortController();
    batch.analysis = { status: 'running', folder, startedAt: new Date().toISOString() };
    const started = performance.now();
    try {
      const result = await runAgent(agent, prompt, { cwd, timeoutMs: 15 * 60000, signal: this.controller.signal });
      await save(path.join(folder, 'instructions.txt'), prompt);
      await save(path.join(folder, 'agent.jsonl'), result.stdout);
      await save(path.join(folder, 'stderr.txt'), result.stderr);
      let report;
      try { report = await readFile(path.join(cwd, 'report.md'), 'utf8'); } catch { report = null; }
      if (report) await save(path.join(folder, 'report.md'), report);
      batch.analysis = { ...batch.analysis, agent, status: result.cancelled ? 'cancelled' : result.timedOut ? 'limit reached' : result.code === 0 && report && !result.denials.length ? 'passed' : 'failed', usage: result.usage, durationMs: Math.round(performance.now() - started), finishedAt: new Date().toISOString(), excludedFromBenchmark: true, hasReport: !!report };
      await save(path.join(folder, 'analysis.json'), batch.analysis);
      batch.analyses = { ...batch.analyses, [agent.agent]: batch.analysis };
    } finally { this.controller = null; this.next(); }
    return batch.analysis;
  }
}
