const SEED = 'rectangle 3x4: 12.0\nrectangle 2x5: 10.0\ncircle r=2: 12.56636\ntotal: 34.56636';
export default async (c) => {
  const r = c.run(['3x4', '2x5', 'circle:2'], { fn: 'Bench.Geo.Cli.main' });
  const src = ['Bench.Geo.Shapes', 'Bench.Geo.Report', 'Bench.Geo.Cli', 'Bench.Geo.Tests'].map(m => c.source(m)).join('\n');
  const sa = c.eval('Bench.Geo.Shapes.surfaceArea(Bench.Geo.Shapes.Shape.Rectangle(Bench.Geo.Shapes.Rect { width = 3, height = 4 }))');
  const old = c.eval('Bench.Geo.Shapes.area(Bench.Geo.Shapes.Shape.Circle(1))');
  const t = c.test('Geo');
  return [
    { name: 'output byte-identical', ok: r.out.trim() === SEED, got: r.out + r.err },
    { name: 'surfaceArea with width/height', ok: sa.out.trim() === '12.0', got: sa.out + sa.err },
    { name: 'no w/h fields or area calls left', ok: !/\b(r|rect|s)\.[wh]\b|\{\s*w\s*[:=]|\bw\s*:\s*Int64|\.area\(/.test(src), got: (src.match(/.{0,30}(\.[wh]\b|\bw\s*:|\.area\().{0,20}/) || [''])[0] },
    { name: 'area gone or deprecated', ok: old.code !== 0 || /deprecat/i.test(old.out + old.err), got: old.out.slice(0, 80) },
    { name: 'tests pass, at least 4', ok: t.passed >= 4 && t.failed === 0, got: JSON.stringify(t).slice(0, 150) },
  ];
};
