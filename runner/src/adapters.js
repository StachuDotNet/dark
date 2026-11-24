import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { command } from './process.js';

// Let Claude edit the project and run shell commands inside its sandbox without prompts.
// Explicit shell approval also covers variables and loops, which can otherwise prompt.
// Reads outside the project are allowed. Network access is limited to the task's sites.
// Block other hosts rather than waiting for approval that this session cannot give.
const claudeSandbox = sites => JSON.stringify({ sandbox: { enabled: true, autoAllowBashIfSandboxed: true, allowUnsandboxedCommands: false,
  strictAllowlist: true, network: { allowedDomains: sites } } });

export const blankUsage = () => ({ inputTokens: null, cachedInputTokens: null, cacheCreationTokens: null, outputTokens: null, reasoningTokens: null, modelCalls: null, costUsd: null });
const number = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
export function eventsFrom(text) {
  return text.split('\n').flatMap(line => { try { return [JSON.parse(line)]; } catch { return []; } });
}

export function parseUsage(agent, events) {
  const usage = blankUsage();
  if (agent === 'codex') {
    const reported = events.filter(e => e.type === 'turn.completed' && e.usage).map(e => e.usage);
    for (const [field, key] of Object.entries({ inputTokens: 'input_tokens', cachedInputTokens: 'cached_input_tokens', outputTokens: 'output_tokens', reasoningTokens: 'reasoning_output_tokens' })) {
      if (reported.length && reported.every(u => number(u[key]) !== null)) usage[field] = reported.reduce((sum, u) => sum + u[key], 0);
    }
    // Codex's turn.completed describes a user turn, not the model calls inside it.
    // Leave modelCalls blank until a supported source reports those calls.
  } else {
    const result = events.findLast(e => e.type === 'result');
    if (result?.usage) {
      const u = result.usage;
      const parts = ['input_tokens', 'cache_read_input_tokens', 'cache_creation_input_tokens'];
      // Anthropic reports these input categories separately. Our input total includes all three.
      usage.inputTokens = parts.every(k => number(u[k]) !== null) ? parts.reduce((s, k) => s + u[k], 0) : null;
      usage.cachedInputTokens = number(u.cache_read_input_tokens);
      usage.cacheCreationTokens = number(u.cache_creation_input_tokens);
      usage.outputTokens = number(u.output_tokens);
      usage.reasoningTokens = number(u.reasoning_tokens);
      usage.costUsd = number(result.total_cost_usd);
    }
    const messages = new Set(events.filter(e => e.type === 'assistant' && e.message?.id).map(e => e.message.id));
    // Count each assistant API message once, even if it has several content blocks.
    if (result && messages.size) usage.modelCalls = messages.size;
  }
  return usage;
}

export function launchArgs(agent, model, dirs = [], sites = []) {
  if (agent === 'codex') {
    const args = ['exec'];
    args.push('--json', '--skip-git-repo-check');
    args.push('--sandbox', 'workspace-write');
    // Codex only reaches the network through its proxy, which allows just the listed sites.
    if (sites.length) args.push('-c', 'sandbox_workspace_write.network_access=true', '-c', 'features.network_proxy.enabled=true',
      '-c', `features.network_proxy.domains={ ${sites.map(site => `${JSON.stringify(site)} = "allow"`).join(', ')} }`);
    for (const dir of dirs) args.push('--add-dir', dir);
    if (model) args.push('--model', model);
    args.push('-');
    return args;
  }
  const args = ['--print', '--output-format', 'stream-json', '--verbose', '--permission-mode', 'acceptEdits'];
  args.push('--permission-prompts', 'none', '--settings', claudeSandbox(sites), '--allowedTools', 'Bash', 'Read', 'Glob', 'Grep');
  if (dirs.length) args.push('--add-dir', ...dirs);
  if (model) args.push('--model', model);
  return args;
}

export async function runAgent(config, prompt, options) {
  const args = launchArgs(config.agent, config.model, options.writableDirs, options.networkSites);
  const result = await command(config.path, args, { ...options, env: options.env, input: prompt });
  const events = eventsFrom(result.stdout);
  const sessionId = events.find(e => e.type === 'thread.started')?.thread_id
    ?? events.find(e => e.session_id)?.session_id;
  const denials = events.flatMap(e => e.permission_denials ?? []);
  const final = events.findLast(e => e.type === 'result');
  return { ...result, args, sessionId, events, usage: parseUsage(config.agent, events), denials,
    agentError: final?.is_error === true || events.some(e => ['turn.failed', 'error'].includes(e.type)),
    errorText: errorText(events, final) };
}
// Use the CLI's error message to explain why the attempt was blocked.
export function errorText(events, final) {
  if (final?.is_error === true && typeof final.result === 'string' && final.result.trim()) return final.result.trim();
  const failed = events.findLast(e => e.type === 'turn.failed')?.error?.message ?? events.findLast(e => e.type === 'error')?.message;
  return typeof failed === 'string' && failed.trim() ? failed.trim() : null;
}

// Send a small request with the batch's launch settings to check the login.
// Version and help commands can succeed even when the login has expired.
export async function checkLogin(config, signal, env) {
  const cwd = await mkdtemp(path.join(os.tmpdir(), 'benchmark-login-check-'));
  try {
    const result = await runAgent(config, 'Reply with the single word OK.', { cwd, timeoutMs: 120000, signal, env });
    const ok = !result.error && !result.timedOut && !result.cancelled && result.code === 0 && !result.agentError;
    return { agent: config.agent, ok, message: ok ? null : result.errorText || result.error || (result.timedOut ? 'No reply within 2 minutes.' : result.stderr.trim() || `Exited with code ${result.code}.`), usage: result.usage };
  } finally { await rm(cwd, { recursive: true, force: true }); }
}
