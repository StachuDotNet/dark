export default async (c) => {
  const runs = [['A-1:2:1250', 'B-7:1:899'], ['E-5:12:300'], ['A-1:1:1250'], ['C-3:10:450', 'D-4:1:1999'], ['B-7:3:899', 'F-6:2:2500']];
  const want = ['3899', '3740', '1750', '6049', '7697'];
  const got = runs.map(a => c.run(a, { fn: 'Bench.Quote.main' }).out.trim());
  const t = c.test('Quote');
  return [
    { name: 'names the run and 6049', ok: /6499/.test(c.final) && /6049/.test(c.final), got: c.final.slice(0, 200) },
    { name: 'fourth run now 6049', ok: got[3] === '6049', got: got[3] },
    { name: 'other runs unchanged', ok: [0, 1, 2, 4].every(i => got[i] === want[i]), got: got.join(',') },
    { name: 'a test runs and passes', ok: t.passed > 0 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) },
  ];
};
