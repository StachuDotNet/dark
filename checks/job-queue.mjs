const sleep = (ms) => new Promise(r => setTimeout(r, ms));
export default async (c) => {
  const port = 18084 + Math.floor(Math.random() * 500);
  const s = await c.serve(port);
  if (!s) return [{ name: 'server starts', ok: false, got: `${c.entry.router || c.entry.main}` }];
  const post = (b) => c.http(port, 'POST', '/jobs', JSON.stringify(b), { 'content-type': 'application/json' });
  const id = (r) => { try { const j = JSON.parse(r.text); return j.id ?? j.jobId ?? j.job?.id; } catch { return null; } };
  const out = [{ name: 'server starts', ok: true }];
  const bad = [await post({ kind: 'nope' }), await post({ kind: 'fib' })];
  out.push({ name: 'bad bodies 400', ok: bad.every(r => r.status === 400), got: bad.map(r => r.status).join(',') });
  const f = await post({ kind: 'fib', n: 25 }); const fid = id(f);
  let fd = null; for (let i = 0; i < 30 && fid != null; i++) { const r = await c.http(port, 'GET', `/jobs/${fid}`); if (/done/.test(r.text)) { fd = r.text; break; } await sleep(500); }
  out.push({ name: 'fib 25 202 then 75025', ok: f.status === 202 && !!fd && fd.includes('75025'), got: `${f.status} ${fd ?? f.text}` });
  const ids = []; for (let i = 0; i < 5; i++) ids.push(id(await post({ kind: 'sleep', seconds: 3 })));
  await sleep(1000);
  const st = await Promise.all(ids.map(i => c.http(port, 'GET', `/jobs/${i}`)));
  const running = st.filter(r => /"running"/.test(r.text)).length, queued = st.filter(r => /"queued"/.test(r.text)).length;
  out.push({ name: '3 running, 2 queued', ok: running === 3 && queued === 2, got: `${running} running, ${queued} queued` });
  await sleep(7000);
  const end = await Promise.all(ids.map(i => c.http(port, 'GET', `/jobs/${i}`)));
  out.push({ name: 'all 5 done by 8 s', ok: end.every(r => /"done"/.test(r.text)), got: end.map(r => r.text.slice(0, 40)).join(' | ') });
  const stats = await c.http(port, 'GET', '/stats');
  out.push({ name: '/stats answers', ok: stats.status === 200 && /done/.test(stats.text), got: stats.text.slice(0, 120) });
  s.stop();
  const t = c.test(c.owner().split('.')[1]);
  out.push({ name: 'tests run and pass', ok: t.passed > 0 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) });
  return out;
};
