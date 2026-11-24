import { realpathSync, existsSync } from 'node:fs';
import { mkdir, copyFile, chmod, readFile, rm } from 'node:fs/promises';
import path from 'node:path';
import { createHash, randomUUID } from 'node:crypto';

// .NET keeps named-lock files here regardless of TMPDIR; Dark needs one to change its permissions.
// Codex's sandbox already allows /tmp, so Claude needs it too for a fair comparison. It holds no package state.
export const dotnetLocks = path.join(realpathSync('/tmp'), '.dotnet');

// Give each batch its own files so earlier runs cannot change its runtime.
export function darkRuntimePaths(config, id, attemptId = null) {
  if (!/^[a-zA-Z0-9-]+$/.test(id)) throw Error('Invalid Dark runtime ID.');
  // Resolve macOS temporary paths because Dark rejects symlinks above its policy folder.
  let ancestor = path.resolve(config.workspaceRoot);
  const missing = [];
  while (!existsSync(ancestor)) { missing.unshift(path.basename(ancestor)); ancestor = path.dirname(ancestor); }
  if (attemptId && !/^[a-f0-9]{32}$/.test(attemptId)) throw Error('Invalid attempt runtime ID.');
  const batchRoot = path.join(realpathSync(ancestor), ...missing, '.batch-tools', id);
  const root = attemptId ? path.join(batchRoot, 'attempts', attemptId) : batchRoot;
  const binaryPath = path.join(root, path.basename(config.darkPath));
  const store = path.join(root, '.darklang');
  const extraction = path.join(root, 'dotnet');
  return { id, root, binaryPath, store, extraction,
    env: { DOTNET_BUNDLE_EXTRACT_BASE_DIR: extraction, DARK_CONFIG_RUNDIR: store },
    writableDirs: [store, dotnetLocks] };
}
export async function prepareDarkRuntime(config, id = randomUUID(), attemptId = null) {
  const runtime = darkRuntimePaths(config, id, attemptId);
  await mkdir(path.dirname(runtime.root), { recursive: true });
  await mkdir(runtime.root, { mode: 0o700 });
  try {
    await copyFile(config.darkPath, runtime.binaryPath);
    await chmod(runtime.binaryPath, 0o500);
    // Copy the files needed to start Dark, but leave its existing data and permissions behind.
    for (const suffix of ['.mode', '.runtimeconfig.json', '.deps.json']) {
      try { await copyFile(config.darkPath + suffix, runtime.binaryPath + suffix); }
      catch (error) { if (error.code !== 'ENOENT') throw error; }
    }
    await mkdir(runtime.store, { mode: 0o700 });
    await mkdir(dotnetLocks, { recursive: true });
    await mkdir(runtime.extraction, { mode: 0o700 });
    return { ...runtime, sourcePath: config.darkPath,
      binarySha256: createHash('sha256').update(await readFile(runtime.binaryPath)).digest('hex'),
      packageBaseline: 'Empty store initialized by this binary; no previous package data copied' };
  } catch (error) { await rm(runtime.root, { recursive: true, force: true }); throw error; }
}

export function darkAttemptId(run) {
  return createHash('sha256').update(JSON.stringify([run.task, run.agent, run.model || '(CLI default)', run.repetition])).digest('hex').slice(0, 32);
}
export function darkAttemptPaths(config, run) {
  return darkRuntimePaths(config, config.runtimeId, darkAttemptId(run));
}
