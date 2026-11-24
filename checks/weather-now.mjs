const net = ['http GET https://geocoding-api.open-meteo.com/*', 'http GET https://api.open-meteo.com/*'];
export default async (c) => {
  const b = c.run(['Berlin'], { allow: net }), sf = c.run(['San Francisco'], { allow: net }), q = c.run(['Qwxzzy'], { allow: net });
  return [
    { name: 'Berlin: °C and wind with units', ok: b.code === 0 && /-?\d+(\.\d+)?\s*°?C/.test(b.out) && /km\/h|m\/s|mph|kn/.test(b.out) && /Berlin/.test(b.out), got: b.out + b.err },
    { name: 'San Francisco works', ok: sf.code === 0 && /San Francisco/.test(sf.out), got: sf.out + sf.err },
    { name: 'unknown city exits 1', ok: q.code === 1, got: `${q.code} ${q.out}${q.err}` },
  ];
};
