import { spawn } from 'node:child_process';

// Use a separate process group so Cancel can stop child processes too.
export function command(executable, args, { cwd, input = '', timeoutMs = 30000, signal, onLine = () => {}, onStderrLine = () => {}, env = {}, inheritEnv = true } = {}) {
  return new Promise(resolve => {
    let stdout = '', stderr = '', buffer = '', stderrBuffer = '', timedOut = false, cancelled = false, finished = false;
    const child = spawn(executable, args, { cwd, detached: true, env: inheritEnv ? { ...process.env, ...env } : env, stdio: ['pipe', 'pipe', 'pipe'] });
    const kill = () => { try { process.kill(-child.pid, 'SIGKILL'); } catch {} };
    const abort = () => { cancelled = true; kill(); };
    const timer = setTimeout(() => { timedOut = true; kill(); }, Math.max(1, timeoutMs));
    signal?.addEventListener('abort', abort, { once: true });
    if (signal?.aborted) abort();
    child.stdin.on('error', () => {});
    child.stdin.end(input);
    child.stdout.on('data', chunk => {
      const text = chunk.toString(); stdout += text; buffer += text;
      let end;
      while ((end = buffer.indexOf('\n')) >= 0) { onLine(buffer.slice(0, end)); buffer = buffer.slice(end + 1); }
    });
    child.stderr.on('data', chunk => {
      const text = chunk.toString(); stderr += text; stderrBuffer += text;
      let end;
      while ((end = stderrBuffer.indexOf('\n')) >= 0) { onStderrLine(stderrBuffer.slice(0, end)); stderrBuffer = stderrBuffer.slice(end + 1); }
    });
    const finish = (code, error) => {
      if (finished) return; finished = true;
      clearTimeout(timer); signal?.removeEventListener('abort', abort);
      if (buffer) onLine(buffer);
      if (stderrBuffer) onStderrLine(stderrBuffer);
      // An agent can exit while a background command is still running.
      kill();
      resolve({ stdout, stderr, code, error: error?.message ?? null, timedOut, cancelled });
    };
    child.on('error', error => finish(null, error));
    child.on('exit', kill);
    child.on('close', code => finish(code));
  });
}
