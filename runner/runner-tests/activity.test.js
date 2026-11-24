import test from 'node:test';
import assert from 'node:assert/strict';
import { createActivityFormatter } from '../src/activity.js';

test('Codex events show messages, commands, failures and changed files without metadata JSON', () => {
  const format = createActivityFormatter(), event = e => format(JSON.stringify(e));
  assert.equal(event({type:'thread.started',thread_id:'private-id'})[0].title, 'Agent session started');
  assert.deepEqual(event({type:'turn.started'}), []);
  assert.equal(event({type:'item.completed',item:{type:'agent_message',text:'I am testing the program.'}})[0].text, 'I am testing the program.');
  const started = event({type:'item.started',item:{type:'command_execution',command:'dark run main.dark'}})[0];
  assert.equal(started.title, 'Running command');
  assert.equal(started.details, 'dark run main.dark');
  const failed = event({type:'item.completed',item:{type:'command_execution',command:'dark run main.dark',exit_code:1,aggregated_output:'Function not found'}})[0];
  assert.equal(failed.kind, 'error');
  assert.equal(failed.text, 'Function not found');
  assert.equal(event({type:'item.completed',item:{type:'file_change',changes:[{kind:'add',path:'main.dark'}]}})[0].text, 'add: main.dark');
  assert.equal(format('a plain CLI diagnostic')[0].text, 'a plain CLI diagnostic');
  assert.equal(format('null')[0].title, 'CLI output');
});

test('Claude tools are matched to results and denied requests remain visible', () => {
  const format = createActivityFormatter(), event = e => format(JSON.stringify(e));
  const tools = event({type:'assistant',message:{content:[{type:'tool_use',id:'one',name:'Bash',input:{command:'python3 main.py',description:'Test normal input'}}]}});
  assert.equal(tools[0].text, 'Test normal input');
  assert.equal(event({type:'user',message:{content:[{type:'tool_result',tool_use_id:'one',is_error:true,content:'Missing module'}]}})[0].title, 'Bash failed');
  event({type:'assistant',message:{content:[{type:'text',text:'Done testing.'}]}});
  assert.equal(event({type:'result',result:'Done testing.'}).length, 1);
  const denied = event({type:'result',permission_denials:[{tool_name:'Bash'}]});
  assert.equal(denied[0].title, 'Permission denied');
  assert.equal(denied.at(-1).kind, 'error');
  assert.deepEqual(event({type:'system',subtype:'hook_started'}), []);
  const long = event({type:'assistant',message:{content:[{type:'text',text:'x'.repeat(5000)}]}})[0].text;
  assert.match(long, /Full output is saved/);
});

test('recent activity is rebuilt from saved stdout and stderr without inventing timestamps', async () => {
  const { mkdtemp, mkdir, writeFile, readFile } = await import('node:fs/promises');
  const { default: os } = await import('node:os');
  const { default: path } = await import('node:path');
  const { savedActivity } = await import('../src/activity.js');
  const folder = await mkdtemp(path.join(os.tmpdir(), 'saved-activity-test-'));
  const runFolder = path.join(folder,'runs','one'); await mkdir(runFolder,{recursive:true});
  const raw = [
    {type:'assistant',timestamp:'2026-10-14T10:00:00Z',message:{content:[{type:'text',text:'Testing the program.'}]}},
    {type:'item.completed',item:{type:'command_execution',command:'dark run main.dark',aggregated_output:'Unknown function',exit_code:1}}
  ].map(e => JSON.stringify(e)).join('\n');
  await writeFile(path.join(runFolder,'agent-0.jsonl'),raw);
  await writeFile(path.join(runFolder,'stderr-0.txt'),'A CLI error\n');
  const events = await savedActivity(folder,[{id:'one',segments:[{}]}]);
  assert.equal(events[0].text,'Testing the program.');
  assert.equal(events[0].at,'2026-10-14T10:00:00Z');
  assert.equal(events[1].title,'Command failed (exit 1)');
  assert.equal(events[1].at,null);
  assert.equal(events[2].text,'A CLI error');
  assert.ok(events.every(e => e.saved));
  assert.equal(await readFile(path.join(runFolder,'agent-0.jsonl'),'utf8'),raw);
  assert.deepEqual(await savedActivity(folder,[{id:'missing'}]),[]);
});
