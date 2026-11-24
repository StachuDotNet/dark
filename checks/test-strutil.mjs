export default async (c) => {
  const t = c.test('Strutil');
  const ev = (e) => c.eval(e).out.trim();
  return [
    { name: 'tests run and pass, at least 5', ok: t.passed >= 5 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) },
    { name: 'truncate fixed', ok: ev('Bench.Strutil.truncate("abcd", 4)') === 'abcd' && ev('Bench.Strutil.truncate("abcdef", 4)') === 'abc…', got: ev('Bench.Strutil.truncate("abcd", 4)') },
    { name: 'others unchanged', ok: ev('Bench.Strutil.slugify("Hello, World!")') === 'hello-world' && ev('Bench.Strutil.countWords("  two   words ")') === '2' && ev('Bench.Strutil.titleCase("the LORD of the rings")') === 'The Lord of the Rings' },
  ];
};
