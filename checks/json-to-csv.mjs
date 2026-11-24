// a CSV reader, to compare rows rather than bytes
export function csv(text) {
  const rows = []; let row = [], cell = '', q = false;
  for (let i = 0; i < text.length; i++) { const ch = text[i];
    if (q) { if (ch === '"' && text[i + 1] === '"') { cell += '"'; i++; } else if (ch === '"') q = false; else cell += ch; }
    else if (ch === '"') q = true; else if (ch === ',') { row.push(cell); cell = ''; } else if (ch === '\n' || ch === '\r') { if (ch === '\r' && text[i + 1] === '\n') i++; row.push(cell); rows.push(row); row = []; cell = ''; } else cell += ch; }
  if (cell !== '' || row.length) { row.push(cell); rows.push(row); }
  return rows;
}
export default async (c) => {
  const conv = (json) => { c.write('in.json', json); return c.run(['in.json'], { cwd: c.tmp }); };
  const eq = (r, rows) => JSON.stringify(csv(r.out.trim())) === JSON.stringify(rows);
  const a = conv('[{"id":1,"name":"Ann"},{"id":2,"city":"Oslo"}]');
  const b = conv('[{"a":{"b":1,"c":{"d":true}}}]');
  const q = conv('[{"t":"x, \\"y\\""}]');
  const arr = conv('[{"tags":["a","b"]}]');
  const n = conv('[{"n":null,"f":1.5}]');
  const errs = ['{"a":1}', '[1,2]', 'not json'].map(conv);
  const stdin = c.run([], { cwd: c.tmp, stdin: '[{"x":1}]' });
  const arrCell = csv(arr.out.trim())[1]?.[0] || '';
  return [
    { name: 'union header, empty cells', ok: eq(a, [['id', 'name', 'city'], ['1', 'Ann', ''], ['2', '', 'Oslo']]), got: a.out + a.err },
    { name: 'nested keys dotted', ok: eq(b, [['a.b', 'a.c.d'], ['1', 'true']]), got: b.out },
    { name: 'quotes doubled', ok: eq(q, [['t'], ['x, "y"']]) && q.out.includes('"x, ""y"""'), got: q.out },
    { name: 'array as JSON text', ok: arrCell.replace(/\s/g, '') === '["a","b"]', got: arr.out },
    { name: 'null is empty', ok: eq(n, [['n', 'f'], ['', '1.5']]), got: n.out },
    { name: 'bad input exits 1', ok: errs.every(r => r.code === 1), got: errs.map(r => r.code).join(',') },
    { name: 'reads stdin', ok: eq(stdin, [['x'], ['1']]), got: stdin.out + stdin.err },
  ];
};
