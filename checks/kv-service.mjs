export default async (c) => {
  const port = 18600 + Math.floor(Math.random() * 300);
  const s = await c.serve(port);
  if (!s) return [{ name: 'server starts', ok: false, got: `${c.entry.router || c.entry.main}` }];
  const h = (m, p, b) => c.http(port, m, p, b);
  const p1 = await h('PUT', '/kv/a', 'hello'), p2 = await h('PUT', '/kv/a', 'world'), g = await h('GET', '/kv/a');
  const miss = await h('GET', '/kv/missing'), d1 = await h('DELETE', '/kv/a'), d2 = await h('DELETE', '/kv/a');
  const b1 = await h('PUT', '/kv/bad%20key', 'x'), b2 = await h('PUT', '/kv/' + 'k'.repeat(65), 'x');
  await h('PUT', '/kv/x', 'abc'); await h('PUT', '/kv/y', '12345'); const all = await h('GET', '/kv');
  s.stop();
  let j = null; try { j = JSON.parse(all.text); } catch {}
  const t = c.test(c.owner().split('.')[1]);
  return [
    { name: 'PUT 201 then 200, GET value', ok: p1.status === 201 && p2.status === 200 && g.status === 200 && g.text === 'world', got: `${p1.status} ${p2.status} ${g.status} ${g.text}` },
    { name: '404 and DELETE 204 then 404', ok: miss.status === 404 && d1.status === 204 && d2.status === 404, got: `${miss.status} ${d1.status} ${d2.status}` },
    { name: 'bad keys 400', ok: b1.status === 400 && b2.status === 400, got: `${b1.status} ${b2.status}` },
    { name: 'GET /kv lengths', ok: j && j.x === 3 && j.y === 5 && Object.keys(j).length === 2, got: all.text },
    { name: 'tests run and pass', ok: t.passed > 0 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) },
  ];
};
