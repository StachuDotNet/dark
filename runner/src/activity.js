import { open } from 'node:fs/promises';
import path from 'node:path';
import { redact } from './storage.js';

// Build the activity feed from events the CLI actually reports.
export function createActivityFormatter() {
  const tools = new Map();
  let lastMessage = '';
  const clip = value => { const text = String(value || ''); return text.length > 4000 ? text.slice(0, 4000) + '\n… Full output is saved in the original log.' : text; };
  const entry = (kind, title, text = '', details = '') => ({ kind, title, text: clip(text), details: clip(details) });
  const contentText = value => typeof value === 'string' ? value : Array.isArray(value) ? value.filter(b => b.type === 'text').map(b => b.text).join('\n') : '';
  return line => {
    let event;
    try { event = JSON.parse(line); } catch { return line.trim() ? [entry('output', 'CLI output', line)] : []; }
    if (!event || typeof event !== 'object') return [entry('output', 'CLI output', line)];
    if (event.type === 'thread.started' || (event.type === 'system' && event.subtype === 'init')) return [entry('status', 'Agent session started')];
    const item = event.item;
    if (item?.type === 'agent_message' && event.type === 'item.completed') {
      lastMessage = item.text; return [entry('message', 'Agent', item.text)];
    }
    if (item?.type === 'command_execution') {
      if (event.type === 'item.started') return [entry('command', 'Running command', '', item.command)];
      if (event.type === 'item.completed') return [entry(item.exit_code === 0 ? 'output' : 'error', item.exit_code === 0 ? 'Command finished' : `Command failed (exit ${item.exit_code ?? 'unknown'})`, item.aggregated_output, item.command)];
    }
    if (item?.type === 'file_change' && event.type === 'item.completed') return [entry('files', 'Changed files', (item.changes || []).map(c => `${c.kind}: ${c.path}`).join('\n'))];
    if (event.type === 'assistant') return (event.message?.content || []).flatMap(block => {
      if (block.type === 'text') { lastMessage = block.text; return [entry('message', 'Agent', block.text)]; }
      if (block.type === 'tool_use') {
        tools.set(block.id, block.name);
        const input = block.input || {};
        return [entry('tool', `Using ${block.name}`, input.description || input.file_path || input.pattern || input.query || '', input.command || '')];
      }
      return [];
    });
    if (event.type === 'user') return (Array.isArray(event.message?.content) ? event.message.content : []).filter(b => b.type === 'tool_result').map(block => entry(block.is_error ? 'error' : 'output', `${tools.get(block.tool_use_id) || 'Tool'} ${block.is_error ? 'failed' : 'result'}`, contentText(block.content)));
    if (event.type === 'result') {
      const rows = [];
      if (event.result && event.result !== lastMessage) rows.push(entry(event.is_error ? 'error' : 'message', event.is_error ? 'Agent error' : 'Agent', event.result));
      for (const denial of event.permission_denials || []) rows.push(entry('error', 'Permission denied', denial.tool_name || 'A tool request was denied.'));
      rows.push(entry(event.is_error || event.permission_denials?.length ? 'error' : 'status', event.is_error ? 'Session ended with an error' : event.permission_denials?.length ? 'Session ended with denied permissions' : 'Agent session finished'));
      return rows;
    }
    if (event.type === 'turn.completed') return [entry('status', 'Agent session finished')];
    if (['error', 'turn.failed'].includes(event.type)) return [entry('error', 'CLI error', event.error?.message || event.message || event.error || 'The CLI reported a failure.')];
    if (event.type === 'rate_limit_event' && event.rate_limit_info?.status !== 'allowed') return [entry('error', 'Rate limit reported', event.rate_limit_info?.status)];
    // Keep session IDs, token details, hook messages and partial output in the full logs only.
    return [];
  };
}

// Reload recent activity without changing the logs or guessing event times.
export async function savedActivity(batchFolder, runs) {
  const events = [];
  const append = (rows, runId, at = null) => {
    for (const row of rows) events.push({ ...row, text: redact(row.text), details: redact(row.details), runId, at, saved: true });
    if (events.length > 150) events.splice(0, events.length - 150);
  };
  const lines = async file => {
    let handle;
    try {
      handle = await open(file);
      const { size } = await handle.stat();
      const start = Math.max(0, size - 4 * 1024 * 1024);
      const buffer = Buffer.alloc(size - start);
      const { bytesRead } = await handle.read(buffer, 0, buffer.length, start);
      let text = buffer.subarray(0, bytesRead).toString('utf8');
      if (start) { const firstNewline = text.indexOf('\n'); text = firstNewline < 0 ? '' : text.slice(firstNewline + 1); }
      return text.split(/\r?\n/).filter(line => line.trim());
    } catch (error) { if (error.code === 'ENOENT') return []; throw error; }
    finally { await handle?.close(); }
  };
  for (const run of runs) {
    if (typeof run.id !== 'string' || !/^[a-zA-Z0-9_-]+$/.test(run.id)) continue;
    const folder = path.join(batchFolder, 'runs', run.id), format = createActivityFormatter();
    for (const segment of run.segments?.length ? run.segments : [{ repair: 0 }]) {
      const index = segment.repair ?? 0;
      for (const line of await lines(path.join(folder, `agent-${index}.jsonl`))) {
        let at = null;
        try { const timestamp = JSON.parse(line).timestamp; if (timestamp && Number.isFinite(Date.parse(timestamp))) at = timestamp; } catch {}
        append(format(line), run.id, at);
      }
      for (const line of await lines(path.join(folder, `stderr-${index}.txt`))) append([{ kind:'error', title:'Saved CLI diagnostic', text:line.slice(0,4000), details:'' }], run.id);
    }
    if (run.error) append([{ kind:'error', title:`Attempt ${run.status}`, text:run.error, details:'' }], run.id);
  }
  return events;
}
