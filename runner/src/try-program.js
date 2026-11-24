import { cp, mkdir, mkdtemp, readFile, realpath, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { command } from './process.js';
import { dotnetLocks } from './dark-runtime.js';
import { redact, save } from './storage.js';

const OUTPUT_LIMIT = 200_000;
const clip = text => text.length > OUTPUT_LIMIT ? text.slice(0, OUTPUT_LIMIT) + '\n[Output truncated]' : text;
const tryFile = (batch, run) => path.join(batch.folder, 'manual-runs', `${run.id}.json`);
const quoted = value => `"${value.replaceAll('\\', '\\\\').replaceAll('"', '\\"')}"`;

// Block home-folder access and limit writes to the temporary project and runtime folders.
// Reads elsewhere and network access stay allowed. Do not pass the full parent environment.
export function sandboxProfile({ home, writable }) {
  return ['(version 1)', '(allow default)',
    `(deny file-read* file-write* (subpath ${quoted(home)}))`,
    '(deny file-write*)',
    `(allow file-write* ${writable.map(dir => `(subpath ${quoted(dir)})`).join(' ')} (literal "/dev/null") (literal "/dev/tty") (regex #"^/dev/fd/"))`].join('\n');
}

// Save manual runs separately so they do not change the benchmark files or hashes.
export async function savedTries(batch, run) {
  try { return JSON.parse(await readFile(tryFile(batch, run), 'utf8')); }
  catch (error) { if (error.code === 'ENOENT') return []; throw error; }
}

// Run a temporary copy inside the sandbox so the original project stays unchanged.
export async function tryProgram(batch, run, { commandLine, input = '', timeoutMs = 60000 } = {}) {
  if (!run.workspace) throw Error('This attempt has no project folder.');
  if (typeof commandLine !== 'string' || !commandLine.trim()) throw Error('Enter a command to run.');
  if (commandLine.length > 4000 || String(input).length > 100_000) throw Error('The command or input is too long.');
  if (process.platform !== 'darwin') throw Error('Sandboxed program runs are only available on macOS for now.');
  let source;
  try { source = await realpath(run.workspace); } catch { throw Error('The project folder is no longer available on disk.'); }
  // The sandbox matches real paths, and macOS's temporary folder is a link into /private.
  const scratch = await realpath(await mkdtemp(path.join(os.tmpdir(), 'benchmark-try-')));
  const cwd = path.join(scratch, 'project'), home = path.join(scratch, 'home'), tmp = path.join(scratch, 'tmp');
  try {
    await cp(source, cwd, { recursive: true, verbatimSymlinks: true });
    await mkdir(home); await mkdir(tmp);
    const env = { PATH: process.env.PATH || '/usr/bin:/bin', LANG: process.env.LANG || 'en_US.UTF-8', TERM: 'dumb', HOME: home, TMPDIR: tmp };
    const writable = [scratch];
    // Dark keeps permission grants in the attempt's store, so reuse it as the agent configured it.
    if (run.language === 'Dark' && run.runtime) {
      Object.assign(env, run.runtime.env, { DARK: run.runtime.binaryPath });
      writable.push(run.runtime.store, run.runtime.extraction, dotnetLocks);
    }
    const profile = sandboxProfile({ home: await realpath(os.homedir()), writable });
    const started = performance.now();
    const result = await command('/usr/bin/sandbox-exec', ['-p', profile, '/bin/sh', '-c', commandLine], { cwd, input: String(input), timeoutMs, env, inheritEnv: false });
    const entry = { at: new Date().toISOString(), command: commandLine, input: String(input), exitCode: result.code,
      timedOut: result.timedOut, error: result.error, durationMs: Math.round(performance.now() - started),
      stdout: redact(clip(result.stdout)), stderr: redact(clip(result.stderr)) };
    const tries = await savedTries(batch, run);
    tries.push(entry);
    await save(tryFile(batch, run), tries);
    return { entry, tries };
  } finally { await rm(scratch, { recursive: true, force: true }); }
}

// A starting point only; the person running it edits the command and arguments.
export function suggestedCommand(language, names) {
  const pick = pattern => names.filter(n => pattern.test(n) && !/(^|\/)(test_|tests?\/)|\.test\.|_test\./.test(n)).sort((a, b) => a.split('/').length - b.split('/').length)[0];
  const quote = name => /^[\w./-]+$/.test(name) ? name : `'${name.replaceAll("'", `'\\''`)}'`;
  if (language === 'Dark') { const file = pick(/\.dark$/); return file ? `"$DARK" run ${quote(file)} ` : '"$DARK" run '; }
  if (language === 'Python') { const file = pick(/\.py$/); return file ? `python3 ${quote(file)} ` : 'python3 '; }
  if (names.includes('package.json')) return 'npm start -- ';
  const file = pick(/\.(ts|mts|js|mjs)$/);
  return file ? `node ${quote(file)} ` : 'node ';
}
