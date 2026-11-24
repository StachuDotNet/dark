export default async (c) => {
  const r = (...a) => { const x = c.run(a); return { ...x, o: x.out.trim() }; };
  const cases = [[['add', '1h30m', '45m'], '2h15m'], [['add', '1d', '1s'], '1d1s'], [['fmt', '0'], '0s'], [['fmt', '90061'], '1d1h1m1s'], [['fmt', '5400'], '1h30m']];
  const got = cases.map(([a]) => r(...a));
  const bad = [['add', '1h1h', '5m'], ['add', '5x', '1m'], ['add', '', '1m']].map(a => r(...a));
  // separate modules: saved apart, or nested `module X =` blocks in one save
  const saved = c.entry.modules.filter(m => !/Tests?$/.test(m));
  const nested = saved.flatMap(m => [...c.source(m).matchAll(/^\s*module (\w+)\s*=/gm)].map(x => `${m}.${x[1]}`)).filter(m => !/Tests?$/.test(m));
  const mods = [...new Set([...saved, ...nested])];
  const fns = c.fns().filter(f => !/Tests?$/.test(f.module) && !/^main$/.test(f.short));
  const described = fns.filter(f => /\/\/\//.test(c.source(f.name)) || /description/i.test(c.dark(['view', f.name]).out));
  const t = c.test(c.owner().split('.')[1]);
  return [
    { name: 'add and fmt answers', ok: cases.every(([, w], i) => got[i].o === w && got[i].code === 0), got: got.map(x => x.o).join(',') },
    { name: 'bad input exits 1', ok: bad.every(x => x.code === 1), got: bad.map(x => x.code).join(',') },
    { name: 'three or more modules', ok: mods.length >= 3, got: mods.join(',') },
    { name: 'public functions described', ok: fns.length > 0 && described.length >= 0.8 * fns.length, got: `${described.length}/${fns.length}` },
    { name: 'tests run and pass', ok: t.passed > 0 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) },
  ];
};
